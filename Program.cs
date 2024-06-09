using System.Net;
using System.Net.Sockets;
using System.Text;
using System.IO.Ports;
using System.Data;
using System.Runtime.CompilerServices;

namespace T41ServerApp;

/*
WSJT-X DXLabSuiteCommanderTransceiver Commands:
  Command structure:
    <command:X>YYYY<parameters:Z><UUUU:V>WWWW<AAAA:B>CCCC
    where:
    X     length of command YYYY
    YYYY  command
    Z     length of parameters
    <UUUU:V>WWWW    parameter 1, UUUU-parameter type, V-length of argument, WWWW-argument
    <AAAA:B>CCCC    parameter 2, AAAA-parameter type, B-length of argument, CCCC-argument

  Commands w/o parameters:
    Command                                           Expected reponse (WSJT-X doesn't use length field, %1, it can be omitted)
    <command:10>CmdGetFreq<parameters:0>              <CmdFreq:%1>yyyy.yyy freq in kHz
    <command:5>CmdTX<parameters:0>                    none
    <command:5>CmdRX<parameters:0>                    none
    <command:9>CmdSendTx<parameters:0>                <CmdTX:%1>ON/OFF
    <command:12>CmdGetTXFreq<parameters:0>            <CmdTXFreq:%1>yyyy.y freq in kHz
    <command:12>CmdSendSplit<parameters:0>            <CmdSplit:%1>ON/OFF
    <command:11>CmdSendMode<parameters:0>             <CmdMode:%1>AM/CW/CW-R/FM/LSB/USB/RTTY/RTTY-R/PKT/DATA-L/Data-L/DIGL/PKT-R/DATA-U/Data-U/DIGU

  Commands w/ parameters (no response expected):
    * %1 is parameter length
    * %2 is argument length

    Command             Typical
    <command:10>CmdSetFreq<parameters:%1><xcvrfreq:%2>yyyy.yyy freq in kHz
                        <command:10>CmdSetFreq<parameters:23><xcvrfreq:10> 7,048.055

    <command:14>CmdSetFreqMode<parameters:%1><xcvrfreq:%2>yyyy.yyy<xcvrmode:%3>zz<preservesplitanddual:1>Y"
                        <command:14>CmdSetFreqMode<parameters:63><xcvrfreq:10> 7,074.000<xcvrmode:3>USB<preservesplitanddual:1>Y

    <command:11>CmdQSXSplit<parameters:%1><xcvrfreq:%2>yyyy.yyy<SuppressDual:1>Y[<SuppressModeChange:1>Y]
    <command:8>CmdSplit<parameters:8><1:3>off
    <command:10>CmdSetMode<parameters:%1><1:%2>yy mode as above
*/

internal class Program {
  static T41Server debugServer = new T41Server(10, 512);
  static IPEndPoint debubIPEndPoint = new(IPAddress.Parse("127.0.0.1"), 48005);

  static T41Server wsjtServer = new T41Server(5, 256);
  static IPEndPoint wsjtIpEndPoint = new(IPAddress.Parse("127.0.0.1"), 52002);

  //static async Task Main(string[] args) {
  static void Main(string[] args) {
    T41Serial t41Serial = new T41Serial();

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

    t41Serial.Init(wsjtServer);

    while(true) {
    }
  }
}
