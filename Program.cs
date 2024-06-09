using System.Net;

namespace T41ServerApp;

internal class Program {
  static T41Server debugServer = new T41Server(10, 512);
  static IPEndPoint debubIPEndPoint = new(IPAddress.Parse("127.0.0.1"), 48005);

  static T41Server wsjtServer = new T41Server(5, 256);
  static IPEndPoint wsjtIpEndPoint = new(IPAddress.Parse("127.0.0.1"), 52002);

  static void Main(string[] args) {
    T41Serial t41Serial = new T41Serial();

    Console.Title = "T41 Server";

    // initialize and start servers
    debugServer.Init(t41Serial);
    debugServer.Start(debubIPEndPoint);

    wsjtServer.Init(t41Serial);
    wsjtServer.Start(wsjtIpEndPoint);

    if(t41Serial.Connect()) {
      Console.WriteLine($"Connected to " + t41Serial.SelectedPort);

      // set T41 time
      t41Serial.SetTime();
    }

    t41Serial.Init(wsjtServer, debugServer);

    while(true) {
    }
  }
}
