using System.Net.Sockets;
using System.Text;

using SocketHelper;
using T41.Serial;

namespace T41.Server.Debug;

class ServerDebug : T41Server {
  private const int numWindows = 11;
  private Socket[] dbSocket = new Socket[numWindows];

  public ServerDebug(SocketSettings theSettings, T41Serial t41serial) : base(theSettings, t41serial) {}

  protected override void RespondToAccept(SocketAsyncEventArgs e) {
    Console.WriteLine("Debug window connection accepted. There are {0} debug windows connected to the server", m_numConnectedSockets);

    // create socket for debug window and send window ID back
    Socket socket = (Socket)e.AcceptSocket;
    int index = NextAvailableSocket();

    dbSocket[index] = socket;
    byte[] byt = new byte[1];
    byt[0] = (byte)(index + 1);
    socket.SendAsync(byt);
  }

  // process debug window message
  // *** called from T41Serial ***
  public void ProcessDebugMessage(string msg, int id) {
    if(id < 100 && dbSocket[id] != null) {
      // *** TODO: verify that this doesn't go back through IO_Completed ***
      dbSocket[id].SendAsync(Encoding.Default.GetBytes(msg), 0);
    } else {
      // handle debug messages without a debug window
      Console.WriteLine($"");
      Console.Write($"***** ");
      Console.Write(msg);
      Console.WriteLine($" *****");
      Console.WriteLine($"");
    }
  }

  protected override void ProcessClose(Socket socket) {
    for(int i = 0; i < numWindows; i++) {
      if(dbSocket[i] == socket) {
        dbSocket[i] = null;
        Console.WriteLine("A debug window disconnected from the server. There are {0} debug windows connected to the server", m_numConnectedSockets);
      }
    }
  }

  private int NextAvailableSocket() {
    for(int i = 0; i < numWindows; i++) {
      if(dbSocket[i] == null) {
        return i;
      }
    }
    // No more room
    // for now just overwrite last spot (very unlikely to occur)
    // *** TODO: consider changing ***
    return numWindows - 1;
  }
}
