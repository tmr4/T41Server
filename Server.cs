using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SocketHelper;

// Socket info: https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket?view=net-8.0

// modified from: https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketasynceventargs?view=net-8.0

// Implements the connection logic for a socket server
// After accepting a connection, the server posts a ReceiveAsync to the connection
class SocketServer {
  SocketSettings settings;

  // represents a large reusable set of buffers for all socket operations
  BufferManager m_bufferManager;

  // read, write (don't alloc buffer space for accepts)
  const int opsToPreAlloc = 2;

  // the socket used to listen for incoming connection requests
  Socket listenSocket;

  // pool of reusable SocketAsyncEventArgs objects for write, read and accept socket operations
  protected SocketAsyncEventArgsPool m_readWritePool;

  // the total number of clients connected to the server
  protected int m_numConnectedSockets;

  Semaphore m_maxNumberAcceptedClients;

  // Create a server instance, initialize and start it
  public SocketServer(SocketSettings theSettings) {
    int numConnections = theSettings.MaxConnections;
    int receiveBufferSize = theSettings.BufSize;

    settings = theSettings;

    m_numConnectedSockets = 0;

    // allocate buffers such that the maximum number of sockets can have one outstanding read and
    //write posted to the socket simultaneously
    m_bufferManager = new BufferManager(receiveBufferSize * numConnections * opsToPreAlloc, receiveBufferSize);

    m_readWritePool = new SocketAsyncEventArgsPool(numConnections);
    m_maxNumberAcceptedClients = new Semaphore(numConnections, numConnections);

    Init();
    Start(settings.EndPoint);
  }

  // Initializes the server by preallocating reusable buffers and
  // context objects.  These objects do not need to be preallocated
  // or reused, but it is done this way to illustrate how the API can
  // easily be used to create reusable objects to increase server performance.
  public void Init() {
    // Allocates one large byte buffer which all I/O operations use a piece of.
    // This gaurds against memory fragmentation
    m_bufferManager.InitBuffer();

    // preallocate pool of SocketAsyncEventArgs objects
    SocketAsyncEventArgs readWriteEventArg;

    for (int i = 0; i < settings.MaxConnections; i++) {
      //Pre-allocate a set of reusable SocketAsyncEventArgs
      readWriteEventArg = new SocketAsyncEventArgs();
      readWriteEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(IO_Completed);

      // assign a byte buffer from the buffer pool to the SocketAsyncEventArg object
      m_bufferManager.SetBuffer(readWriteEventArg);

      // add SocketAsyncEventArg to the pool
      m_readWritePool.Push(readWriteEventArg);
    }
  }

  // Starts the server and begin listening for incoming connection requests.
  public void Start(IPEndPoint localEndPoint) {
    // create the socket which listens for incoming connections
    listenSocket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
    listenSocket.Bind(localEndPoint);

    // start the server with a listen backlog of 100 connections
    // *** TODO: move listen parameter to server settings ***
    listenSocket.Listen(100);

    // post accepts on the listening socket
    SocketAsyncEventArgs acceptEventArg = new SocketAsyncEventArgs();
    acceptEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(AcceptEventArg_Completed);
    StartAccept(acceptEventArg);
  }

  // Begins an operation to accept a connection request from the client
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
  void AcceptEventArg_Completed(object sender, SocketAsyncEventArgs e) {
    ProcessAccept(e);

    // Accept the next connection request
    StartAccept(e);
  }

  // This method is called whenever a receive or send operation is completed on a socket
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

  protected virtual void ProcessAccept(SocketAsyncEventArgs e) {

    Interlocked.Increment(ref m_numConnectedSockets);
    Console.WriteLine("Client connection accepted. There are {0} clients connected to the server", m_numConnectedSockets);

    // Get the socket for the accepted client connection and put it into the
    // ReadEventArg object user token
    SocketAsyncEventArgs readEventArgs = m_readWritePool.Pop();
    readEventArgs.UserToken = e.AcceptSocket;

    // As soon as the client is connected, post a receive to the connection
    bool willRaiseEvent = e.AcceptSocket.ReceiveAsync(readEventArgs);
    if (!willRaiseEvent) {
      ProcessReceive(readEventArgs);
    }
  }

  // This method is invoked when an asynchronous receive operation completes.
  // If the remote host closed the connection, then the socket is closed.
  // If data was received then the data is processed according to the client type.
  protected void ProcessReceive(SocketAsyncEventArgs e) {
    // check if the remote host closed the connection
    if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success) {
      Socket socket = (Socket)e.UserToken;

      // process data
      ProcessData(e);

      // read the next block of data sent from the client
      bool willRaiseEvent = socket.ReceiveAsync(e);
      if (!willRaiseEvent) {
        ProcessReceive(e);
      }
    } else {
      CloseClientSocket(e);
    }
  }

  // process data received from ReceiveAsync
  // Server displays received data to the console
  // override this method to perform customized processing
  protected virtual void ProcessData(SocketAsyncEventArgs e) {
    string response;

    response = Encoding.UTF8.GetString(e.Buffer, e.Offset, e.BytesTransferred);
    Console.WriteLine("");
    Console.WriteLine("Received from port {0}: {1}", ((IPEndPoint)((Socket)e.UserToken).RemoteEndPoint).Port, response);
    Console.WriteLine("");
  }

  // This method is invoked when an asynchronous send operation completes.
  // If the remote host closed the connection, then the socket is closed,
  // otherwise the SocketAsyncEventArgs is returned to the pool for reuse.
  private void ProcessSend(SocketAsyncEventArgs e) {
    if (e.SocketError == SocketError.Success) {
      // respond to send completion
      AfterSend(e);

      // return SocketAsyncEventArgs to pool for reuse
      m_readWritePool.Push(e);
    } else {
      CloseClientSocket(e);
    }
  }

  // respond to the completion of SendAsync
  protected virtual void AfterSend(SocketAsyncEventArgs e) {
    // Server does not respond to the completion of a async send
    // override this method to perform customized processing
  }

  protected void CloseClientSocket(SocketAsyncEventArgs e) {
    Socket socket = (Socket)e.UserToken;

    // decrement the counter keeping track of the total number of clients connected to the server
    Interlocked.Decrement(ref m_numConnectedSockets);
    ProcessClose(socket);

    // close the socket associated with the client
    try {
      socket.Shutdown(SocketShutdown.Send);
    }
    // throws if client process has already closed
    catch (Exception) { }
    socket.Close();

    // Free the SocketAsyncEventArg so they can be reused by another client
    m_readWritePool.Push(e);

    m_maxNumberAcceptedClients.Release();
  }

  protected virtual void ProcessClose(Socket socket) {
    Console.WriteLine("A client disconnected from the server. There are {0} clients connected to the server", m_numConnectedSockets);
  }
}
