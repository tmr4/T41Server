using System.Net.Sockets;

using SocketHelper;
using T41.Serial;

namespace T41.Server;

// adds T41Serial connection to server
class T41Server : SocketServer {
  // serial class
  protected T41Serial t41Serial;
  int connectRetryCount = 0;

  public T41Server(SocketSettings theSettings, T41Serial t41serial) : base(theSettings) {
    t41Serial = t41serial;
  }

  protected override void ProcessAccept(SocketAsyncEventArgs e) {
    // do we have a serial connection?
    if(t41Serial.Connected) {
      Interlocked.Increment(ref m_numConnectedSockets);

      //  perform additional work in response to connection
      RespondToAccept(e);

      // Get the socket for the accepted client connection and put it into the
      // ReadEventArg object user token
      SocketAsyncEventArgs readEventArgs = m_readWritePool.Pop();
      readEventArgs.UserToken = e.AcceptSocket;

      // As soon as the client is connected, post a receive to the connection
      bool willRaiseEvent = e.AcceptSocket.ReceiveAsync(readEventArgs);
      if (!willRaiseEvent) {
        ProcessReceive(readEventArgs);
      }
    } else {
      if(connectRetryCount < 10) {
        if(connectRetryCount++ == 0) {
        }
        Thread.Sleep(10000);
        ProcessAccept(e);
      } else {
        Console.WriteLine("\nClient attempted to connect before connected to T41.");
        CloseClientSocket(e);
      }
    }
  }

  protected virtual void RespondToAccept(SocketAsyncEventArgs e) {
    Console.WriteLine("Client connection accepted. There are {0} clients connected to the server", m_numConnectedSockets);
  }
}
