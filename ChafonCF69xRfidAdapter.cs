namespace rfPro.Iot.Core.Drivers;

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

class ChafonCF69xRfidAdapter : IDisposable {
    
    private readonly byte[] CLOSE_RELAY_COMMAND = { 0xCF, 0xFF, 0x00, 0x77, 0x02, 0x02, 0x00, 0x5D, 0x26 };
    private readonly byte[] OPEN_RELAY_COMMAND = { 0xCF, 0xFF, 0x00, 0x77, 0x02, 0x01, 0x00, 0x77, 0x4E };
    private readonly byte[] GET_PARAMS_COMMAND = { 0xCF, 0xFF, 0x00, 0x70, 0x00, 0x24, 0x15 };

    private readonly Socket client;

    private readonly int repeatInterval;
    private readonly int pulseInterval;
    private readonly Dictionary<string, long> delayBag = new();
    private readonly IPEndPoint ipEndPoint;
    private readonly string _epcPrefix;
    private readonly bool _debug;
    private readonly string _host;

    private NetworkStream? networkStream;

    public event EventHandler<RfidEventArgs>? OnDataReceived;

    public ChafonCF69xRfidAdapter(string host, int port, int repeatSecondsInterval, int pulseInterval, bool debug = false, string epcPrefix = "")
    {
        _host = host;
        IPAddress ipAddress = IPAddress.Parse(host);
        ipEndPoint = new(ipAddress, port);
        this.repeatInterval = repeatSecondsInterval - 1;
        this.pulseInterval = pulseInterval;
        _epcPrefix = epcPrefix;
        _debug = debug;

        client = new(
            ipEndPoint.AddressFamily,
            SocketType.Stream,
            ProtocolType.Tcp
        );
    }

    long GetEpoch() => (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;

    public async Task<ChafonCF69xRfidAdapter> Start() {
        _ = Task.Run(Loop);
        _ = Task.Run(Ping);

        return this;
    }

    private async Task Ping()
    {
        while (true)
        {
            await Task.Delay(15000);
            
            if (_debug)
                Console.WriteLine($"[{DateTime.Now}] Ping Connected={networkStream?.Socket.Connected}...");

            if (!Tcp.PingHost(_host))
            {
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }

            if (!CheckAntennaConnection().Result)
            {
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }
        }
    }

    private async Task<bool> CheckAntennaConnection()
    {
        try
        {
            var buffer = new byte[1024];

            if (networkStream != null && networkStream.CanWrite)
            {
                networkStream.Write(GET_PARAMS_COMMAND, 0, GET_PARAMS_COMMAND.Length);
                networkStream.Flush();

                _ = await networkStream.ReadAsync(buffer, CancellationToken.None);
                
                return true;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[{DateTime.Now}] CheckAntennaConnection: error connecting to antenna: {e.Message}");
        }

        return false;
    }

    private void CloseNetworkStream()
    {
        if (networkStream != null)
        {
            networkStream.Close();
            networkStream.Dispose();
        }

        if (client.Connected)
        {
            client.Close();
        }
    }

    private async Task InitNetworkStream()
    {
        try
        {
            CloseNetworkStream();
            
            await client.ConnectAsync(ipEndPoint);
            Console.WriteLine($"[{DateTime.Now}] Connected to device {ipEndPoint.Address}.");
            
            networkStream = new NetworkStream(client);
            var buffer = new byte[1024];

            if (networkStream != null && networkStream.CanWrite)
            {
                networkStream.Write(GET_PARAMS_COMMAND, 0, GET_PARAMS_COMMAND.Length);
                networkStream.Flush();

                var byteLength = await networkStream.ReadAsync(buffer, CancellationToken.None);
                string parameters = Encoding.ASCII.GetString(buffer, 0, byteLength);
                Console.WriteLine($"[{DateTime.Now}] {parameters}");

                Console.WriteLine($"[{DateTime.Now}] Listening...");
                
                return;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[{DateTime.Now}] error connecting to antenna: {e.Message}");
        }

        await Task.Delay(5000);
        Console.WriteLine($"[{DateTime.Now}] Try to connect...");
        await InitNetworkStream();
    }

    private async Task Loop()
    {
        await InitNetworkStream();
        
        var buffer = new byte[1024];

        while (true)
        {
            List<byte> lstByte = new();
            try
            {
                var received = await networkStream.ReadAsync(buffer, CancellationToken.None);
                for (int i = 0; i < received; i++)
                {
                    lstByte.Add(buffer[i]);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[{DateTime.Now}] error receiving packages: {e.Message}");
                
                await Task.Delay(3000);
                await InitNetworkStream();
            }
            
            List<byte> unparsed = lstByte.Count > 25 ? lstByte.GetRange(0, 25) : lstByte;
            if (unparsed.Count == 25)
            {
                string epc = BitConverter.ToString(unparsed.GetRange(11, 12).ToArray());

                if (String.IsNullOrEmpty(_epcPrefix) || epc.StartsWith(_epcPrefix))
                {
                    int rssi = unparsed[unparsed.Count - 1] * -1;

                    long lastSeenTime = 0;
                    if (delayBag.ContainsKey(epc))
                    {
                        lastSeenTime = delayBag[epc];
                        long now = GetEpoch();
                        if ((now - lastSeenTime) > repeatInterval)
                        {
                            delayBag[epc] = now;

                            OnDataReceived?.Invoke(this, new RfidEventArgs() { Epc = epc, Rssi = rssi });
                        }
                    }
                    else
                    {
                        delayBag.Add(epc, GetEpoch());

                        OnDataReceived?.Invoke(this, new RfidEventArgs() { Epc = epc, Rssi = rssi });
                    }
                }
            }
        }
    }

    public async Task Pulse() 
    {
        if (networkStream != null && networkStream.CanWrite)
        {
            networkStream.Write(CLOSE_RELAY_COMMAND, 0, CLOSE_RELAY_COMMAND.Length);
            networkStream.Flush();
            await Task.Delay(this.pulseInterval);
            networkStream.Write(OPEN_RELAY_COMMAND, 0, OPEN_RELAY_COMMAND.Length);
            networkStream.Flush();
            await Task.Delay(500);
        }
    }

    public void Dispose() 
    { 
        client?.Shutdown(SocketShutdown.Both);
        client?.Dispose();
    }

}
