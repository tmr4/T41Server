using System.Net.Sockets;
using System.Text;

using SocketHelper;
using T41.Serial;

namespace T41.Server.WSJTX;

class ServerWSJTX : T41Server {
  Socket replyAwaitingSocket = null;

  public ServerWSJTX(SocketSettings theSettings, T41Serial t41serial) : base(theSettings, t41serial) {
  }

  protected override void RespondToAccept(SocketAsyncEventArgs e) {
    Console.WriteLine("WSJT-X connection accepted");
  }

  protected override void ProcessData(SocketAsyncEventArgs e) {
    string command = Encoding.Default.GetString(e.Buffer, e.Offset, e.BytesTransferred);

    // process command
    bool awaitingT41Reply = ProcessWsjtxCommand(command);

    // save token if we're expecting a reply from the T41
    // we'll use this to send a response back to WSJT-X
    if (awaitingT41Reply) {
      replyAwaitingSocket = (Socket)e.UserToken;
    }
  }

  // process command received from WSJT-X
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

  public bool ProcessWsjtxCommand(string commandString) {
    bool awaitingT41Reply = false;
    string command = "";

    double fq;
    long freq;
    string mode;
    int index, colon, len, demod;

    if(commandString.IndexOf("<command:") == 0) {
      // we have the start of a command, parse it
      Console.WriteLine($"Received command from WSJT-X: \"{commandString}\"");

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
  // *** called from T41Serial ***
  public void ProcessReply(string command) {
    string response = "";
    double fq;
    int index, colon, len, demod;

    // Get a new SocketAsyncEventArgs and put reply awaiting socket in user token
    SocketAsyncEventArgs replyEventArgs = m_readWritePool.Pop();
    replyEventArgs.UserToken = replyAwaitingSocket;

    // process command
    switch(command) {
      case "CmdGetFreq": // get current VFO frequency from T41
      case "CmdGetTXFreq": // get transmission frequency, only used in split state
        // *** TODO: just returning VFO A for now ***
        fq = ((double)t41Serial.Freq) / 1000;
        response = "<CmdFreq:>" + fq.ToString("F3");
        break;

      case "CmdSendSplit": // get split state from T41
        response = "<CmdSplit:>" + t41Serial.Split;
        break;

      case "CmdSendMode": // get demod mode from T41
        response = "<CmdMode:>" + t41Serial.Mode;
        break;

      case "":
      default:
        break;
    }

    // send reply to WSJT-X
    Console.WriteLine($"   Sending reply to WSJT-X: \"{response}\"");

    // copy response
    Array.Copy(Encoding.Default.GetBytes(response), 0, replyEventArgs.Buffer, replyEventArgs.Offset, response.Length);

    // set the buffer for this reply (restricts transfer to our reply)
    replyEventArgs.SetBuffer(replyEventArgs.Offset, response.Length);

    // send reply
    bool willRaiseEvent = replyAwaitingSocket.SendAsync(replyEventArgs);

    // reset socket if above was completed synchronously
    if (!willRaiseEvent) {
      // return SocketAsyncEventArgs to pool for reuse
      m_readWritePool.Push(replyEventArgs);
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

  protected override void ProcessClose(Socket socket) {
    Console.WriteLine("WSJT-X disconnected from the server");
  }
}
