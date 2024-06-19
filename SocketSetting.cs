using System.Net;

namespace SocketHelper;

class SocketSettings {
  private IPEndPoint endPoint;

  // the maximum number of connections the sample is designed to handle simultaneously
  private int m_numConnections;

  // buffer size to use for each socket I/O operation
  private int m_receiveBufferSize;

  public IPEndPoint EndPoint {
    get { return endPoint; }
  }

  public int MaxConnections {
    get { return m_numConnections; }
  }

  public int BufSize {
    get { return m_receiveBufferSize; }
  }

  public SocketSettings(string ipAddress, int port, int bufSize, int connections = 0) {
    endPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);
    m_numConnections = connections;
    m_receiveBufferSize = bufSize;
  }
}
