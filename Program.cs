using System.Net;
using System.Net.Sockets;
using System.Text;
using System.IO.Ports;
using System.Data;

namespace T41Server;

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
  static Socket listener;
  static IPEndPoint ipEndPoint = new(IPAddress.Parse("127.0.0.1"), 52002);

  // serial port data
  static bool connectionStarted = false;
  static string selectedPort = "";
  static SerialPort? serialPort = null;

  static string[] Ports { get; set; } = SerialPort.GetPortNames();

  // local T41 data
  static long freq = 7048000;
  static string split = "OFF";
  static string mode = "LSB";

  // comms flags
  static bool wsjtAwaitingResponse = false;
  //static string awaitingReponse = "";
  static bool awaitingT41Reply = false;
  static bool t41Replied = false;

  static async Task Main(string[] args) {
    int received;
    string response = "";
    string command = "";

    listener = new Socket(ipEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
    listener.Bind(ipEndPoint);
    listener.Listen(100);


    if(Connect()) {
      Console.WriteLine($"Connected to " + selectedPort);

      // set T41 time
      SetTime();
    }

    Socket wsjtSocket = await listener.AcceptAsync();

    while(true) {
      byte[] buffer = new byte[1_024];
      double fq;
      int index, colon, len, demod;

      if(!wsjtAwaitingResponse) {
        // await message from WSJT-X
        received = await wsjtSocket.ReceiveAsync(buffer, SocketFlags.None);
        response = Encoding.UTF8.GetString(buffer, 0, received);

        if(response.IndexOf("<command:") == 0) {
          // we have the start of a command, parse it
          Console.WriteLine($"Received command from WSJT-X: \"{response}\"");

          colon = response.IndexOf(":");
          index = response.IndexOf(">");
          len = int.Parse(response.Substring(colon + 1, index - colon - 1));

          command = response.Substring(index + 1, len);
        }
      }

      // process command
      // this could be the same command as a previous loop
      // if we're waiting for a reply from the T41
      switch(command) {
        case "CmdGetFreq": // get current VFO frequency from T41
          // *** TODO: just returning VFO A for now ***
          // are we awaiting a reply from the T41?
          if(awaitingT41Reply) {
            if(t41Replied) {
              t41Replied = false;
              fq = ((double)freq) / 1000;
              response = "<CmdFreq:>" + fq.ToString("F3");
              Console.WriteLine($"   Sending reply to WSJT-X: \"{response}\"");
              await wsjtSocket.SendAsync(Encoding.UTF8.GetBytes(response), 0);
              awaitingT41Reply = false;
              wsjtAwaitingResponse = false;
              command = "";
            }
          } else {
            // send FA to T41
            SendCmd("FA;");
            wsjtAwaitingResponse = true;
            awaitingT41Reply = true;
          }
          break;

        case "CmdTX": // set T41 PTT on
          // send TX to T41
          SendCmd("TX;");
          command = "";
          break;

        case "CmdRX": //set T41 PTT off
          // send RX to T41
          SendCmd("RX;");
          command = "";
          break;

        case "CmdSendTx": // get PTT state from T41
          // always follows the above two
          command = "";
          break;

        case "CmdGetTXFreq": // get transmission frequency, only used in split state
          // *** TODO: just returning VFO A for now ***
          // are we awaiting a reply from the T41?
          if(awaitingT41Reply) {
            if(t41Replied) {
              t41Replied = false;
              fq = ((double)freq) / 1000;
              response = "<CmdFreq:>" + fq.ToString("F3");
              Console.WriteLine($"   Sending reply to WSJT-X: \"{response}\"");
              await wsjtSocket.SendAsync(Encoding.UTF8.GetBytes(response), 0);
              awaitingT41Reply = false;
              wsjtAwaitingResponse = false;
              command = "";
            }
          } else {
            // send FA to T41
            SendCmd("FA;");
            wsjtAwaitingResponse = true;
            awaitingT41Reply = true;
          }
          break;

        case "CmdSendSplit": // get split state from T41
          // are we awaiting a reply from the T41?
          if(awaitingT41Reply) {
            if(t41Replied) {
              t41Replied = false;
              response = "<CmdSplit:>" + split;
              Console.WriteLine($"   Sending reply to WSJT-X: \"{response}\"");
              await wsjtSocket.SendAsync(Encoding.UTF8.GetBytes(response), 0);
              awaitingT41Reply = false;
              wsjtAwaitingResponse = false;
              command = "";
            }
          } else {
            // Kenwood TS-2000 manual mentions but doesn't document the split mode command (SP)
            // The Kenwood TS-890S manual does but is unclear
            // I'll use a returned value of 1=On and 0=Off
            // send SP to T41
            SendCmd("SP;");
            wsjtAwaitingResponse = true;
            awaitingT41Reply = true;
          }
          break;

        case "CmdSendMode": // get demod mode from T41
          // are we awaiting a reply from the T41?
          if(awaitingT41Reply) {
            if(t41Replied) {
              t41Replied = false;
              response = "<CmdMode:>" + mode;
              Console.WriteLine($"   Sending reply to WSJT-X: \"{response}\"");
              await wsjtSocket.SendAsync(Encoding.UTF8.GetBytes(response), 0);
              awaitingT41Reply = false;
              wsjtAwaitingResponse = false;
              command = "";
            }
          } else {
            // send MD to T41
            SendCmd("MD;");
            wsjtAwaitingResponse = true;
            awaitingT41Reply = true;
          }
          break;

        case "CmdSetFreq": // get current VFO frequency from T41
          // just assume VFO A for now
          index = response.LastIndexOf(">");
          fq = double.Parse(response.Substring(index + 1));
          freq = (long)(fq * 1000);
          SendCmd("FA" + freq.ToString("D11"));
          command = "";
          break;

        case "CmdSetFreqMode": // set T41 frequency and demod mode
          // set frequency
          index = response.IndexOf("<xcvrfreq:");
          colon = index + 9;
          index = response.IndexOf(">", colon);
          len = int.Parse(response.Substring(colon + 1, index - colon - 1));
          fq = double.Parse(response.Substring(index + 1, len));
          freq = (long)(fq * 1000);
          SendCmd("FA" + freq.ToString("D11"));

          // set demod
          index = response.IndexOf("<xcvrmode:");
          colon = index + 9;
          index = response.IndexOf(">", colon);
          len = int.Parse(response.Substring(colon + 1, index - colon - 1));
          index = response.IndexOf(">", index);

          mode = response.Substring(index + 1, len);
          demod = GetDemod(mode);

          SendCmd("MD" + demod.ToString("D1"));
          command = "";
          break;

        case "CmdQSXSplit": // *** TODO: unclear when this is issued ***
          break;

        case "CmdSplit": // *** TODO: unclear what T41 change is intended ***
          // only issued when transmission frequency is 0 Hz
          // the parameter of this command is always off
          break;

        case "CmdSetMode": // change T41 demod mode
          index = response.LastIndexOf(">");
          colon = response.LastIndexOf(":");
          len = int.Parse(response.Substring(colon + 1, index - colon - 1));

          mode = response.Substring(index + 1, len);
          demod = GetDemod(mode);

          SendCmd("MD" + demod.ToString("D1"));
          command = "";
          break;

        case "":
        default:
          break;
      }
    }
  }

  // using Kenwood modes here
  // 1: LSB, 2: USB, 3: CW, 4: FM, 5: AM
  static int GetDemod(string mode) {
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
  static bool Connect(string port = "") {
    bool result = false;
    string requestedPort = "";

    if(port == "") {
      Console.Write("Enter COM port to connect to: ");
      requestedPort = Console.ReadLine();
    } else {
      requestedPort = port;
    }

    try {
      // can't reconnect with this
      // *** TODO: work out a way to reconnect if connection is lost ***
      if (!connectionStarted && requestedPort != selectedPort) {
        // try to connect to requestedPort
        serialPort = new SerialPort();
        serialPort.PortName = requestedPort;

        // we have a USB serial port communicating at native USB speeds
        // these aren't used
        serialPort.BaudRate = 19200;
        serialPort.Parity = Parity.None;
        serialPort.DataBits = 8;
        serialPort.DtrEnable = false;
        serialPort.RtsEnable = false;

        try {
          serialPort.Open();
        }

        //catch (Exception ex) {
        catch (Exception) {
          connectionStarted = false;
          selectedPort = "";
          return result;
        }

        Thread.Sleep(30);

        if (serialPort.IsOpen) {
          connectionStarted = true;
          selectedPort = requestedPort;
          result = true;

          serialPort.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);
        } else {
          connectionStarted = false;
          selectedPort = "";
        }
      }
      return result;
    }

    //catch (Exception ex) {
    catch (Exception) {
      connectionStarted = false;
      selectedPort = "";
      return result;
    }
  }

  static void SetTime() {
    // *** TODO: the T41 time will lag from this by the serial processing time, add a second for now ***
    SendCmd("TM" + (DateTimeOffset.Now.ToUnixTimeSeconds() - 7 * 60 * 60 + 1).ToString("D11"));
  }

  static void SendCmd(string cmd) {
    if (serialPort != null && serialPort.IsOpen) {
      Console.WriteLine($"   Sending " + cmd + " to T41");
      serialPort.Write(cmd + ";");
    }
  }

  // Serial port DataReceived events are handled in a secondary thread
  // (https://learn.microsoft.com/en-us/dotnet/api/system.io.ports.serialport.datareceived?view=net-8.0)
  // this says to "post change requests back using Invoke, which will do the work on the proper thread"
  // I couldn't get Invoke or events to work as it seems with winui 3 these remain on the current thread.
  // Using the main UI thread dispatcher for changes to bound properties does work.
  // (amazing how difficult it is to find information on this online)
  static void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e) {
    SerialPort sp = (SerialPort)sender;
    int len = sp.BytesToRead;

    if(len > 0) {
      byte[] byt = new byte[len];

      sp.Read(byt, 0, len);

      // *** assuming here that a single command per buffer ***
      if(len < 512) {
        // execute command
        switch((char)byt[0]) {
          case 'F':
            if((char)byt[1] == 'A' && (char)byt[13] == ';') {
              long f;
              if(FetchFreq(byt, out f)) {
                freq = f;
                Console.WriteLine($"   Received FA" + f.ToString() + "; " + "from T41");
                t41Replied = true;
              }
            }
            break;

          case 'M':
            if((char)byt[1] == 'D' && (char)byt[3] == ';') {
              int val;
              if(FetchInt(byt, 2, out val)) {
                switch(val) {
                  case 0: // USB
                    mode = "USB";
                    break;

                  case 1: // LSB
                    mode = "LSB";
                    break;

                  default:
                    mode = "USB";
                    break;
                }
                Console.WriteLine($"   Received MD" + val.ToString() + "; " + "from T41");
                t41Replied = true;
              }
            }
            break;

          case 'S':
            if((char)byt[1] == 'P' && (char)byt[3] == ';') {
            // Kenwood TS-2000 manual mentions but doesn't document the split mode command (SP)
            // The Kenwood TS-890S manual does but is unclear
            // I'll use a returned value of 1=On and 0=Off
              int val;
              if(FetchInt(byt, 2, out val)) {
                switch(val) {
                  case 0: // off
                    split = "OFF";
                    break;

                  case 1: // on
                    split = "ON";
                    break;

                  default:
                    split = "OFF";
                    break;
                }
                Console.WriteLine($"   Received SP" + val.ToString() + "; " + "from T41");
                t41Replied = true;
              }
            }
            break;

          default:
            break;
        }
      }
    }
  }

  public static bool FetchFreq(byte[] byt, out long result) {
    return long.TryParse(Encoding.Default.GetString(byt, 2, 11), out result);
  }
  public static bool FetchLong(byte[] byt, int start, int len, out long result) {
    return long.TryParse(Encoding.Default.GetString(byt, start, len), out result);
  }
  public static bool FetchInt(byte[] byt, int start, int len, out int result) {
    return int.TryParse(Encoding.Default.GetString(byt, start, len), out result);
  }
  public static bool FetchInt(byte[] byt, int start, out int result) {
    return int.TryParse(Encoding.Default.GetString(byt, start, 1), out result);
  }
  public static bool FetchFreq(string cmd, out long result) {
    return long.TryParse(cmd.Substring(2, 11), out result);
  }
  public static bool FetchLong(string cmd, int start, int len, out long result) {
    return long.TryParse(cmd.Substring(start, len), out result);
  }
  public static bool FetchInt(string cmd, int start, int len, out int result) {
    return int.TryParse(cmd.Substring(start, len), out result);
  }
  public static bool FetchInt(string cmd, int start, out int result) {
    return int.TryParse(cmd.Substring(start, 1), out result);
  }
}
