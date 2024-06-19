using System.Net;

using SocketHelper;
using T41.Server.Debug;
using T41.Server.WSJTX;
using T41.Serial;

namespace T41ServerApp;

internal class Program {
  static void Main(string[] args) {
    ConsoleKeyInfo cki;

    ServerDebug debugServer;
    ServerWSJTX wsjtServer;
    SocketSettings debugSettings = new("127.0.0.1", 48005, 512, 10);
    SocketSettings wsjtSettings = new("127.0.0.1", 52002, 256, 5);

    T41Serial t41Serial = new T41Serial();

    Console.Title = "T41 Server";

    if(t41Serial.Connect()) {
      Console.WriteLine($"Connected to " + t41Serial.SelectedPort);

      debugServer = new ServerDebug(debugSettings, t41Serial);

      wsjtServer = new ServerWSJTX(wsjtSettings, t41Serial);

      t41Serial.Init(wsjtServer, debugServer);

      // set T41 time
      t41Serial.SetTime();

      Console.WriteLine("T41 Server started....");
      Console.WriteLine("Press Escape to terminate the server when done....");
      do {
        cki = Console.ReadKey();
      } while(cki.Key != ConsoleKey.Escape);

      // *** TODO: close down anything needed ***
    } else {
      Console.WriteLine("Failed to connect to T41");
      Console.WriteLine("Press any key to terminate....");
      Console.ReadKey();
    }
  }
}
