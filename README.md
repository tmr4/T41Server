# T41 Server

T41 Server is a Windows console app designed to facilitate communication between the T41 and multiple PC applications over a single USB serial connection.

![T41 Server](https://github.com/tmr4/T41Server/blob/main/images/T41Server.png)

The app currently does the following:

  * Connects WSJT-X to the T41 using the *DX Lab Suite Commander* interface.
    * Split VFO operation not yet supported.
  * Connects to multiple [T41 Debug Windows](https://github.com/tmr4/T41Debug) to display debug messages sent from the T41.

![T41 Server and Debug Windows w/ WSJT-X](https://github.com/tmr4/T41Server/blob/main/images/t41Server_Debug.png)

This is a work in progress.  Still to come is adding communications to my T41 PC control and Beacon Monitor apps.

## Build

Create a new Windows console app project in Visual Studio 2022 and replace the autogenerated C# file with the C# files in this repository.  Build as normal.  The executable file will be located in the bin folder.

## Use

Requires my [T41_SDR](https://github.com/tmr4/T41_SDR) software which must be compiled with one of the USB Types that includes both `Serial` and `Audio` (audio over USB isn't required if you connect the audio from the T41 to your PC in another way). Enter the T41 COM port on starting the server.  Select *DX Lab Suite Commander* as the *Rig* on the WSJT-X Settings *Radio* tab.
