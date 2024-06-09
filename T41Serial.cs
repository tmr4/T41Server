using System.Net.Sockets;
using System.Text;
using System.IO.Ports;

namespace T41ServerApp;

class T41Serial {
  // serial port data
  private bool connectionStarted = false;
  private string selectedPort = "";
  public string SelectedPort { get; }
  private SerialPort? serialPort = null;

  // local T41 data
  public long Freq { get; private set; } = 7048000;
  public string Split { get; private set; } = "OFF";
  public string Mode { get; private set; } = "LSB";

  private Socket[] dbSocket = new Socket[101];
  private int dbCount = 0;

  private T41Server wsjtServer, debugServer;

  public T41Serial() {
  }

  public void Init(T41Server server1, T41Server server2) {
    wsjtServer = server1;
    debugServer = server2;
  }

  public void SetDebugSocket(Socket socket) {
    dbSocket[dbCount++] = socket;
  }

  public bool Connect(string port = "") {
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

          serialPort.DataReceived += new SerialDataReceivedEventHandler(T41DataReceivedHandler);
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

  public void SetTime() {
    // *** TODO: the T41 time will lag from this by the serial processing time, add a second for now ***
    SendCmd("TM" + (DateTimeOffset.Now.ToUnixTimeSeconds() - 7 * 60 * 60 + 1).ToString("D11"));
  }

  public void SendCmd(string cmd) {
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
  public void T41DataReceivedHandler(object sender, SerialDataReceivedEventArgs e) {
    SerialPort sp = (SerialPort)sender;
    int len = sp.BytesToRead;

    if(len > 0) {
      byte[] byt = new byte[len];

      sp.Read(byt, 0, len);

      if(len < 512) {
        int first=0, end=0; //next=0, last=0;
        byte[] cmd = new byte[len];

        // we might have more than one command
        byt.CopyTo(cmd, 0);
        do {
          byte[] msg = new byte[len];

          //last = Array.LastIndexOf(cmd, (byte)'<');

          first = Array.IndexOf(cmd, (byte)'<');
          if(first == -1) {
            // assume we'll only get one non-debug msg per buffer
            first = 0;
            end = len - 1;
          } else if(first != 0) {
            end = first - 1;
            first = 0;
          } else {
            end = Array.IndexOf(cmd, (byte)'>');
          }

          // execute command
          switch((char)cmd[0]) {
            case '<':
              if((char)cmd[end] == '>') {
              int id;
                if(FetchInt(cmd, 1, 2, out id)) {
                  debugServer.ProcessDebugMessage(Encoding.Default.GetString(cmd, 3, end + 1 - 4), id);
                }
              }
              break;

            case 'F':
              if((char)cmd[1] == 'A' && (char)cmd[13] == ';') {
                long f;
                if(FetchFreq(cmd, out f)) {
                  Freq = f;
                  Console.WriteLine($"   Received FA" + f.ToString() + "; " + "from T41");
                  wsjtServer.ProcessReply("CmdGetFreq");
                }
              }
              break;

            case 'M':
              if((char)cmd[1] == 'D' && (char)cmd[3] == ';') {
                int val;
                if(FetchInt(cmd, 2, out val)) {
                  switch(val) {
                    case 0: // USB
                      Mode = "USB";
                      break;

                    case 1: // LSB
                      Mode = "LSB";
                      break;

                    default:
                      Mode = "USB";
                      break;
                  }
                  Console.WriteLine($"   Received MD" + val.ToString() + "; " + "from T41");
                  wsjtServer.ProcessReply("CmdSendMode");
                }
              }
              break;

            case 'S':
              if((char)cmd[1] == 'P' && (char)cmd[3] == ';') {
              // Kenwood TS-2000 manual mentions but doesn't document the split mode command (SP)
              // The Kenwood TS-890S manual does but is unclear
              // I'll use a returned value of 1=On and 0=Off
                int val;
                if(FetchInt(cmd, 2, out val)) {
                  switch(val) {
                    case 0: // off
                      Split = "OFF";
                      break;

                    case 1: // on
                      Split = "ON";
                      break;

                    default:
                      Split = "OFF";
                      break;
                  }
                  Console.WriteLine($"   Received SP" + val.ToString() + "; " + "from T41");
                  wsjtServer.ProcessReply("CmdSendSplit");
                }
              }
              break;

            default:
              break;
          }
          if(end + 1 < len) {
            Array.Copy(cmd, end + 1, cmd, 0, len - end - 1);
            Array.Fill(cmd, (byte)0, len - end - 1, end);
          }
          len = len - end - 1;
        } while (len > 0);
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
