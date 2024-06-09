using System.Net;
using System.Net.Sockets;
using System.Text;
using System.IO.Ports;

namespace T41ServerApp;

// modified from: https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketasynceventargs?view=net-8.0
// Implements the connection logic for the socket server.
// After accepting a connection, all data is prossessed according to the client type.
// This pattern is continued until the client disconnects.
class T41Server {
  private int m_numConnections;   // the maximum number of connections the sample is designed to handle simultaneously
  private int m_receiveBufferSize;// buffer size to use for each socket I/O operation
  BufferManager m_bufferManager;  // represents a large reusable set of buffers for all socket operations
  const int opsToPreAlloc = 2;    // read, write (don't alloc buffer space for accepts)
  Socket listenSocket;            // the socket used to listen for incoming connection requests
  // pool of reusable SocketAsyncEventArgs objects for write, read and accept socket operations
  SocketAsyncEventArgsPool m_readWritePool;
  int m_totalBytesRead;           // counter of the total # bytes received by the server
  int m_numConnectedSockets;      // the total number of clients connected to the server
  Semaphore m_maxNumberAcceptedClients;

  // serial class
  private T41Serial t41Serial;

  // Create an uninitialized server instance.
  // To start the server listening for connection requests
  // call the Init method followed by Start method
  //
  // <param name="numConnections">the maximum number of connections the sample is designed to handle simultaneously</param>
  // <param name="receiveBufferSize">buffer size to use for each socket I/O operation</param>
  public T41Server(int numConnections, int receiveBufferSize) {
    m_totalBytesRead = 0;
    m_numConnectedSockets = 0;
    m_numConnections = numConnections;
    m_receiveBufferSize = receiveBufferSize;
    // allocate buffers such that the maximum number of sockets can have one outstanding read and
    //write posted to the socket simultaneously
    m_bufferManager = new BufferManager(receiveBufferSize * numConnections * opsToPreAlloc, receiveBufferSize);

    m_readWritePool = new SocketAsyncEventArgsPool(numConnections);
    m_maxNumberAcceptedClients = new Semaphore(numConnections, numConnections);
  }

  // Initializes the server by preallocating reusable buffers and
  // context objects.  These objects do not need to be preallocated
  // or reused, but it is done this way to illustrate how the API can
  // easily be used to create reusable objects to increase server performance.
  //
  public void Init(T41Serial t41serial) {
    t41Serial = t41serial;

    // Allocates one large byte buffer which all I/O operations use a piece of.  This gaurds
    // against memory fragmentation
    m_bufferManager.InitBuffer();

    // preallocate pool of SocketAsyncEventArgs objects
    SocketAsyncEventArgs readWriteEventArg;

    for (int i = 0; i < m_numConnections; i++) {
      //Pre-allocate a set of reusable SocketAsyncEventArgs
      readWriteEventArg = new SocketAsyncEventArgs();
      readWriteEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(IO_Completed);

      // assign a byte buffer from the buffer pool to the SocketAsyncEventArg object
      m_bufferManager.SetBuffer(readWriteEventArg);

      // add SocketAsyncEventArg to the pool
      m_readWritePool.Push(readWriteEventArg);
    }
  }

  // Starts the server such that it is listening for
  // incoming connection requests.
  //
  // <param name="localEndPoint">The endpoint which the server will listening
  // for connection requests on</param>
  public void Start(IPEndPoint localEndPoint) {
    // create the socket which listens for incoming connections
    listenSocket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
    listenSocket.Bind(localEndPoint);
    // start the server with a listen backlog of 100 connections
    listenSocket.Listen(100);

    // post accepts on the listening socket
    SocketAsyncEventArgs acceptEventArg = new SocketAsyncEventArgs();
    acceptEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(AcceptEventArg_Completed);
    StartAccept(acceptEventArg);

    //Console.WriteLine("{0} connected sockets with one outstanding receive posted to each....press any key", m_outstandingReadCount);
    //Console.WriteLine("Press any key to terminate the server process....");
    //Console.ReadKey();
  }

  // Begins an operation to accept a connection request from the client
  //
  // <param name="acceptEventArg">The context object to use when issuing
  // the accept operation on the server's listening socket</param>
  public void StartAccept(SocketAsyncEventArgs acceptEventArg) {
    // loop while the method completes synchronously
    bool willRaiseEvent = false;
    while (!willRaiseEvent) {
      m_maxNumberAcceptedClients.WaitOne();

      // socket must be cleared since the context object is being reused
      acceptEventArg.AcceptSocket = null;
      willRaiseEvent = listenSocket.AcceptAsync(acceptEventArg);
      if (!willRaiseEvent) {
          ProcessAccept(acceptEventArg);
      }
    }
  }

  // This method is the callback method associated with Socket.AcceptAsync
  // operations and is invoked when an accept operation is complete
  //
  void AcceptEventArg_Completed(object sender, SocketAsyncEventArgs e) {
    ProcessAccept(e);

    // Accept the next connection request
    StartAccept(e);
  }

  private void ProcessAccept(SocketAsyncEventArgs e) {
    Interlocked.Increment(ref m_numConnectedSockets);
    Console.WriteLine("Debug window connection accepted. There are {0} clients connected to the server", m_numConnectedSockets);

    // Get the socket for the accepted client connection and put it into the
    //ReadEventArg object user token
    SocketAsyncEventArgs readEventArgs = m_readWritePool.Pop();
    readEventArgs.UserToken = e.AcceptSocket;

    // As soon as the client is connected, post a receive to the connection
    bool willRaiseEvent = e.AcceptSocket.ReceiveAsync(readEventArgs);
    if (!willRaiseEvent) {
      ProcessReceive(readEventArgs);
    }
  }

  // This method is called whenever a receive or send operation is completed on a socket
  //
  // <param name="e">SocketAsyncEventArg associated with the completed receive operation</param>
  void IO_Completed(object sender, SocketAsyncEventArgs e) {
    // determine which type of operation just completed and call the associated handler
    switch (e.LastOperation) {
      case SocketAsyncOperation.Receive:
        ProcessReceive(e);
        break;
      case SocketAsyncOperation.Send:
        ProcessSend(e);
        break;
      default:
        throw new ArgumentException("The last operation completed on the socket was not a receive or send");
    }
  }

  SocketAsyncEventArgs readEventArgsAwaiting = null;

  // This method is invoked when an asynchronous receive operation completes.
  // If the remote host closed the connection, then the socket is closed.
  // If data was received then the data is processed according to the client type.
  //
  // Currently only WSJT-X should get here, so process it's command
  //
  private void ProcessReceive(SocketAsyncEventArgs e) {
    // check if the remote host closed the connection
    if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success) {
      //string response = "<CmdFreq:>" + (7074).ToString("F3");
      string command = Encoding.Default.GetString(e.Buffer, e.Offset, e.BytesTransferred);
      Console.WriteLine($"Received command from WSJT-X: \"{command}\"");

      //increment the count of the total bytes receive by the server
      Interlocked.Add(ref m_totalBytesRead, e.BytesTransferred);
      //Console.WriteLine("The server has read a total of {0} bytes", m_totalBytesRead);

      // process command
      bool awaitingT41Reply = ProcessCommand(command);
      //Console.WriteLine($"   Sending reply to WSJT-X: \"{response}\"");

      //echo the data received back to the client
      //e.SetBuffer(e.Offset, e.BytesTransferred);
      //Socket socket = (Socket)e.UserToken;
      //bool willRaiseEvent = socket.SendAsync(e);
      //if (!willRaiseEvent) {

      // reset socket if we're not expecting a reply from the T41
      if (!awaitingT41Reply) {
        ProcessSend(e);
      } else {
        readEventArgsAwaiting = e;
      }
    } else {
      CloseClientSocket(e);
    }
  }

  // This method is invoked when an asynchronous send operation completes.
  // The method issues another receive on the socket to read any additional
  // data sent from the client
  //
  // <param name="e"></param>
  private void ProcessSend(SocketAsyncEventArgs e) {
    if (e.SocketError == SocketError.Success) {
      // done echoing data back to the client
      Socket socket = (Socket)e.UserToken;
      // read the next block of data send from the client
      bool willRaiseEvent = socket.ReceiveAsync(e);
      if (!willRaiseEvent) {
        ProcessReceive(e);
      }
    } else {
      CloseClientSocket(e);
    }
  }

  private void CloseClientSocket(SocketAsyncEventArgs e) {
    Socket socket = (Socket)e.UserToken;

    // close the socket associated with the client
    try {
      socket.Shutdown(SocketShutdown.Send);
    }
    // throws if client process has already closed
    catch (Exception) { }
    socket.Close();

    // decrement the counter keeping track of the total number of clients connected to the server
    Interlocked.Decrement(ref m_numConnectedSockets);

    // Free the SocketAsyncEventArg so they can be reused by another client
    m_readWritePool.Push(e);

    m_maxNumberAcceptedClients.Release();
    Console.WriteLine("A debug window disconnected from the server. There are {0} clients connected to the server", m_numConnectedSockets);
  }

  // process command received from WSJT-X
  // *** TODO: extract this into a WSJT-X class ***
  public bool ProcessCommand(string commandString) {
    bool awaitingT41Reply = false;
    string command = "";

    double fq;
    long freq;
    string mode;
    int index, colon, len, demod;

    if(commandString.IndexOf("<command:") == 0) {
      // we have the start of a command, parse it
      Console.WriteLine($"Received command from WSJT-X: \"{command}\"");

      colon = commandString.IndexOf(":");
      index = commandString.IndexOf(">");
      len = int.Parse(commandString.Substring(colon + 1, index - colon - 1));

      command = commandString.Substring(index + 1, len);
    }

    // process command
    switch(command) {
      case "CmdGetFreq": // get current VFO frequency from T41
        // *** TODO: just returning VFO A for now ***
        t41Serial.SendCmd("FA;");
        awaitingT41Reply = true;
        break;

      case "CmdTX": // set T41 PTT on
        t41Serial.SendCmd("TX;");
        break;

      case "CmdRX": //set T41 PTT off
        t41Serial.SendCmd("RX;");
        break;

      case "CmdSendTx": // get PTT state from T41
        // always follows the above two
        break;

      case "CmdGetTXFreq": // get transmission frequency, only used in split state
        // *** TODO: just returning VFO A for now ***
        t41Serial.SendCmd("FA;");
        awaitingT41Reply = true;
        break;

      case "CmdSendSplit": // get split state from T41
        // Kenwood TS-2000 manual mentions but doesn't document the split mode command (SP)
        // The Kenwood TS-890S manual does but is unclear
        // I'll use a returned value of 1=On and 0=Off
        t41Serial.SendCmd("SP;");
        awaitingT41Reply = true;
        break;

      case "CmdSendMode": // get demod mode from T41
        t41Serial.SendCmd("MD;");
        awaitingT41Reply = true;
        break;

      case "CmdSetFreq": // get current VFO frequency from T41
        // just assume VFO A for now
        index = commandString.LastIndexOf(">");
        fq = double.Parse(commandString.Substring(index + 1));
        freq = (long)(fq * 1000);
        t41Serial.SendCmd("FA" + freq.ToString("D11"));
        break;

      case "CmdSetFreqMode": // set T41 frequency and demod mode
        // set frequency
        index = commandString.IndexOf("<xcvrfreq:");
        colon = index + 9;
        index = commandString.IndexOf(">", colon);
        len = int.Parse(commandString.Substring(colon + 1, index - colon - 1));
        fq = double.Parse(commandString.Substring(index + 1, len));
        freq = (long)(fq * 1000);
        t41Serial.SendCmd("FA" + freq.ToString("D11"));

        // set demod
        index = commandString.IndexOf("<xcvrmode:");
        colon = index + 9;
        index = commandString.IndexOf(">", colon);
        len = int.Parse(commandString.Substring(colon + 1, index - colon - 1));
        index = commandString.IndexOf(">", index);

        mode = commandString.Substring(index + 1, len);
        demod = GetDemod(mode);

        t41Serial.SendCmd("MD" + demod.ToString("D1"));
        break;

      case "CmdQSXSplit": // *** TODO: unclear when this is issued ***
        break;

      case "CmdSplit": // *** TODO: unclear what T41 change is intended ***
        // only issued when transmission frequency is 0 Hz
        // the parameter of this command is always off
        break;

      case "CmdSetMode": // change T41 demod mode
        index = commandString.LastIndexOf(">");
        colon = commandString.LastIndexOf(":");
        len = int.Parse(commandString.Substring(colon + 1, index - colon - 1));

        mode = commandString.Substring(index + 1, len);
        demod = GetDemod(mode);

        t41Serial.SendCmd("MD" + demod.ToString("D1"));
        break;

      case "":
      default:
        break;
    }
    return awaitingT41Reply;
  }

  // process reply from T41 in response to command received from WSJT-X
  public void ProcessReply(string command) {
    string commandString = "";
    int count = readEventArgsAwaiting.Count; // save buffer max size to restore later
    double fq;
    int index, colon, len, demod;

    // process command
    switch(command) {
      case "CmdGetFreq": // get current VFO frequency from T41
      case "CmdGetTXFreq": // get transmission frequency, only used in split state
        // *** TODO: just returning VFO A for now ***
        fq = ((double)t41Serial.Freq) / 1000;
        commandString = "<CmdFreq:>" + fq.ToString("F3");
        break;

      case "CmdSendSplit": // get split state from T41
        commandString = "<CmdSplit:>" + t41Serial.Split;
        break;

      case "CmdSendMode": // get demod mode from T41
        commandString = "<CmdMode:>" + t41Serial.Mode;
        break;

      case "":
      default:
        break;
    }

    // send reply to WSJT-X
    Console.WriteLine($"   Sending reply to WSJT-X: \"{commandString}\"");

    // a reply will always be shorter than the command so we
    // can reuse the SocketAsyncEventArgs buffer allocated
    Array.Copy(Encoding.Default.GetBytes(commandString), 0, readEventArgsAwaiting.Buffer, readEventArgsAwaiting.Offset, commandString.Length);

    // set the buffer for this reply (restricts transfer to our reply)
    readEventArgsAwaiting.SetBuffer(readEventArgsAwaiting.Offset, commandString.Length);

    // get a new socket from last receive
    Socket socket = (Socket)readEventArgsAwaiting.UserToken;

    // send reply
    bool willRaiseEvent = socket.SendAsync(readEventArgsAwaiting);

    // reset socket if above was completed synchronously
    if (!willRaiseEvent) {
      // reset buffer max size
      // if we don't do this future requests are limited to the size of our command
      readEventArgsAwaiting.SetBuffer(readEventArgsAwaiting.Offset, count);
      ProcessSend(readEventArgsAwaiting);
    }
  }

  // using Kenwood modes here
  // 1: LSB, 2: USB, 3: CW, 4: FM, 5: AM
  public int GetDemod(string mode) {
    int demod = 2;

    switch(mode) {
      case "LSB":
        demod = 1; // LSB
        break;

      case "":
        break;

      case "USB":
      default:
        demod = 2; // USB
        break;
    }
    return demod;
  }
}


// from: https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketasynceventargs.-ctor?view=net-8.0
class SocketAsyncEventArgsPool {
  Stack<SocketAsyncEventArgs> m_pool;

  // Initializes the object pool to the specified size
  //
  // The "capacity" parameter is the maximum number of
  // SocketAsyncEventArgs objects the pool can hold
  public SocketAsyncEventArgsPool(int capacity) {
    m_pool = new Stack<SocketAsyncEventArgs>(capacity);
  }

  // Add a SocketAsyncEventArg instance to the pool
  //
  //The "item" parameter is the SocketAsyncEventArgs instance
  // to add to the pool
  public void Push(SocketAsyncEventArgs item) {
    if (item == null) { throw new ArgumentNullException("Items added to a SocketAsyncEventArgsPool cannot be null"); }
    lock (m_pool) {
      m_pool.Push(item);
    }
  }

  // Removes a SocketAsyncEventArgs instance from the pool
  // and returns the object removed from the pool
  public SocketAsyncEventArgs Pop() {
    lock (m_pool) {
      return m_pool.Pop();
    }
  }

  // The number of SocketAsyncEventArgs instances in the pool
  public int Count {
    get { return m_pool.Count; }
  }
}

// modified from: https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketasynceventargs.setbuffer?view=net-8.0#system-net-sockets-socketasynceventargs-setbuffer(system-byte()-system-int32-system-int32)
// This class creates a single large buffer which can be divided up
// and assigned to SocketAsyncEventArgs objects for use with each
// socket I/O operation.
// This enables bufffers to be easily reused and guards against
// fragmenting heap memory.
//
// The operations exposed on the BufferManager class are not thread safe.
class BufferManager
{
    int m_numBytes;                 // the total number of bytes controlled by the buffer pool
    byte[] m_buffer;                // the underlying byte array maintained by the Buffer Manager
    Stack<int> m_freeIndexPool;     //
    int m_currentIndex;
    int m_bufferSize;

    public BufferManager(int totalBytes, int bufferSize)
    {
        m_numBytes = totalBytes;
        m_currentIndex = 0;
        m_bufferSize = bufferSize;
        m_freeIndexPool = new Stack<int>();
    }

    // Allocates buffer space used by the buffer pool
    public void InitBuffer()
    {
        // create one big large buffer and divide that
        // out to each SocketAsyncEventArg object
        m_buffer = new byte[m_numBytes];
    }

    // Assigns a buffer from the buffer pool to the
    // specified SocketAsyncEventArgs object
    //
    // <returns>true if the buffer was successfully set, else false</returns>
    public bool SetBuffer(SocketAsyncEventArgs args)
    {

        if (m_freeIndexPool.Count > 0)
        {
            args.SetBuffer(m_buffer, m_freeIndexPool.Pop(), m_bufferSize);
        }
        else
        {
            if ((m_numBytes - m_bufferSize) < m_currentIndex)
            {
                return false;
            }
            args.SetBuffer(m_buffer, m_currentIndex, m_bufferSize);
            m_currentIndex += m_bufferSize;
        }
        return true;
    }

    // Removes the buffer from a SocketAsyncEventArg object.
    // This frees the buffer back to the buffer pool
    public void FreeBuffer(SocketAsyncEventArgs args)
    {
        m_freeIndexPool.Push(args.Offset);
        args.SetBuffer(null, 0, 0);
    }
}
