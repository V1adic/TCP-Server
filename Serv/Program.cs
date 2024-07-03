using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

public class Messages(string IP)
{
    public string? Message { get; set; }
    public (int, int)? Services { get; set; } // Key -> размер в битах одной части, value -> количество частей.
    public string? IP { get; set; } = IP;
}

class Program
{
    public static List<(string, Messages)> messages = []; // Журнал
    public static List<string> comands = ["/name", "/err", "/messageCL", "/updateCL", "/messageSERV", "/getIPCL", "/check", "/fileServices", "/get_ip_SERV_CL", "/get_ip_SERV"]; // Список команд сервера
    public static Dictionary<string, string> users = []; // Key -> name, value -> IP
    public static string MyDirectory = Directory.GetCurrentDirectory();
    public static string FileUsers = $"{MyDirectory}/Data/Users.txt"; // Name IP
    public static string GroupChat = $"{MyDirectory}/Data/GroupChat.txt"; // Name:IP message

    static void Init() // Correct Work
    {
        StreamReader sr = new(FileUsers);
        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            var temp = line.Split(" ");
            users.Add(temp[0], temp[1]);
        }
        sr.Close();
    }

    public static async Task Main(string[] args)
    {
        Init();
        int port = 11000;
        Console.WriteLine("Запуск сервера....");
        using (TcpServer server = new TcpServer(port))
        {
            Task servertask = server.ListenAsync();
            while (true)
            {
                string? input = Console.ReadLine();
                if (input == "stop")
                {
                    Console.WriteLine("Остановка сервера...");
                    server.Stop();
                    break;
                }
            }
            await servertask;
        }
        Console.WriteLine("Нажмите любую клавишу для выхода...");
        Console.ReadKey(true);
    }
}

class TcpServer : IDisposable
{
    private readonly TcpListener _listener;
    public static List<Connection> _clients = []; // это пул подключений, нужен чтобы нормально отключить всех подключенных при остановке сервера
    bool disposed;

    public TcpServer(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
    }

    

    public async Task ListenAsync()
    {
        try
        {
            _listener.Start();
            Console.WriteLine("Сервер стартовал на " + _listener.LocalEndpoint);

            while (true)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync();
                Console.WriteLine("Подключение: " + client.Client.RemoteEndPoint + " > " + client.Client.LocalEndPoint);
                Connection? con = null;
                lock (_clients)
                {
                    con = new Connection(client, c => {_clients.Remove(c); c.Dispose();});
                    _clients.Add(con);
                }
                
                Console.WriteLine(con?._remoteEndPoint?.ToString()?.Split(":")[0]);
                foreach(var (name, IP) in Program.users)
                {
                    if(IP == con?._remoteEndPoint?.ToString()?.Split(":")[0])
                    {
                        await con.SendMessageAsync(Encoding.UTF8.GetBytes("/yourName " + name));
                        break;
                    }
                }
            }
        }
        catch (SocketException)
        {
            Console.WriteLine("Сервер остановлен.");
        }
    }

    public void Stop()
    {
        _listener.Stop();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (disposed)
            throw new ObjectDisposedException(typeof(TcpServer).FullName);
        disposed = true;
        _listener.Stop();
        if (disposing)
        {
            lock(_clients)
            {
                if (_clients.Count > 0)
                {
                    Console.WriteLine("Отключаю клиентов...");
                    foreach (Connection client in _clients)
                    {
                        client.Dispose();
                    }
                    Console.WriteLine("Клиенты отключены.");
                }
            }
        }
    }

    ~TcpServer() => Dispose(false);
}

class Connection : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    public readonly EndPoint? _remoteEndPoint;
    private readonly Task _readingTask;
    private readonly Task _writingTask;
    private readonly Action<Connection> _disposeCallback;
    private readonly Channel<byte[]> _channel;
    bool disposed;

    public Connection(TcpClient client, Action<Connection> disposeCallback)
    {
        _client = client;
        _stream = client.GetStream();
        _remoteEndPoint = client.Client.RemoteEndPoint;
        _disposeCallback = disposeCallback;
        _channel = Channel.CreateUnbounded<byte[]>();
        _readingTask = RunReadingLoop();
        _writingTask = RunWritingLoop();
    }

    private async Task RunReadingLoop()
    {
        await Task.Yield(); 
        try
        {
            byte[] headerBuffer = new byte[4];
            while (true)
            {
                int bytesReceived = await _stream.ReadAsync(headerBuffer, 0, 4);
                Console.WriteLine(Encoding.UTF8.GetString(headerBuffer)); // TODO DELETE
                if (bytesReceived != 4)
                    break;
                int length = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer);
                byte[] buffer = new byte[length];
                int count = 0;
                while (count < length)
                {
                    bytesReceived = await _stream.ReadAsync(buffer, count, buffer.Length - count);
                    count += bytesReceived;
                }
                string message = Encoding.UTF8.GetString(buffer);

                var services = message.Split(" ", 2);
                switch(services[0])
                {
                    case "/WORK":
                    {
                        await SendMessageAsync(Encoding.UTF8.GetBytes("WORKERS"));

                    }break;

                    case "/updateCL":
                    {
                        using (StreamReader reader = new StreamReader(Program.GroupChat))
                        {
                            byte[] data = Encoding.UTF8.GetBytes("/updateSERV " + reader.ReadToEnd());
                            await SendMessageAsync(data);
                        }
                        Console.WriteLine("Сообщение с информацией о чате отправлено!!!");

                    } break;

                    case "/name":
                    {
                        if (Program.users.TryAdd(services[1], _remoteEndPoint.ToString().Split(":")[0]))
                        {
                            StreamWriter sw = new(Program.FileUsers, append:true);
                            sw.WriteLine($"{services[1]} {_remoteEndPoint?.ToString()?.Split(":")[0]}");
                            sw.Close();
                            byte[] data = Encoding.UTF8.GetBytes("/corr correct");
                            await SendMessageAsync(data);
                            Console.WriteLine("ИМЯ ПРИНЯТО!!!");
                        }
                        else
                        {
                            byte[] data = Encoding.UTF8.GetBytes("/err name is already taken");
                            await SendMessageAsync(data);
                            Console.WriteLine("ИМЯ ЗАНЯТО!!!");
                        }

                    }break;

                    case "/messageCL": 
                    {
                        Console.WriteLine("SAVE IN FILE");
                        using (StreamWriter sw = File.AppendText(Program.GroupChat))
                        {
                            sw.WriteLine(services[1]);
                        }
                        Console.WriteLine(TcpServer._clients.Count);

                        lock(TcpServer._clients)
                        {
                            Console.WriteLine(TcpServer._clients.Count);
                            foreach(var client_receive in TcpServer._clients)
                            {
                                if(client_receive._remoteEndPoint != _remoteEndPoint)
                                {
                                    Console.WriteLine($"Сообщение с по адресу {client_receive._remoteEndPoint}");
                                    Task t = client_receive.SendMessageAsync(Encoding.UTF8.GetBytes("/messageSERV " + services[1]));
                                }
                            }
                        }
                    }break;

                    case "/fileServices": // {/fileServices name filename <END>byteArray}
                    {
                        var fileServices = services[1].Split(" ", 3);
                        var indexOf = 0;
                        List<byte> result = [];
                        bool flag = false;

                        for(int i = 0; i < buffer.Length && !flag ; i++)
                        {
                            result.Add(buffer[i]);
                            if(Encoding.UTF8.GetString(result.ToArray<byte>()).Contains("<END>"))
                            {
                                indexOf = i + 1;
                                flag = true;
                            }
                        }
                        var IP_Client = "";
                        foreach(var (name, IP) in Program.users)
                        {
                            if(name == fileServices[0])
                            {
                                IP_Client = IP;
                            }
                        }

                        lock(TcpServer._clients)
                        {
                            foreach(var client_receive in TcpServer._clients)
                            {
                                if(client_receive?._remoteEndPoint?.ToString()?.Split(":")[0] == IP_Client)
                                {
                                    byte[] a2 = buffer[indexOf .. ];
                                    byte[] a1 = Encoding.UTF8.GetBytes($"/fileServices {fileServices[1]} <END>");
                                    byte[] rv = new byte[a1.Length + a2.Length]; // Concat two byte array
                                    Buffer.BlockCopy(a1, 0, rv, 0, a1.Length);
                                    Buffer.BlockCopy(a2, 0, rv, a1.Length, a2.Length);
                                    Task t = client_receive.SendMessageAsync(rv);
                                }
                            }
                        }

                    }break;

                    case "/getIPCL":
                    {
                        Console.WriteLine("Сообщение с информацией об IP участника пытвется быть отправленным!!!");

                        if (Program.users.TryGetValue(services[1], out string? IP))
                        {
                            if (IP != null)
                            {
                                bool flag = false;
                                lock(TcpServer._clients)
                                {
                                    foreach(var client_receive in TcpServer._clients)
                                    {
                                        if(client_receive?._remoteEndPoint?.ToString()?.Split(":")[0] == IP)
                                        {
                                            flag = true;
                                        }
                                    }
                                }

                                if(flag)
                                {
                                    byte[] data = Encoding.UTF8.GetBytes("/corrOnline");
                                    Console.WriteLine("Сообщение с информацией об IP участника пытвется быть отправленным!!!");
                                    await SendMessageAsync(data);
                                    Console.WriteLine(IP);
                                }
                                else
                                {
                                    byte[] data = Encoding.UTF8.GetBytes("/err NOT ONLINE");
                                    await SendMessageAsync(data);
                                    Console.WriteLine("Сообщение с ошибкой об IP участника сети отправлено как недоступного!!!");
                                }
                            }
                            else
                            {
                                byte[] data = Encoding.UTF8.GetBytes("/err NOT FOUND");
                                await SendMessageAsync(data);
                                Console.WriteLine("Сообщение с ошибкой об IP участника сети отправлено!!!");
                            }
                        }
                        else
                        {
                            byte[] data = Encoding.UTF8.GetBytes("/err NOT FOUND");
                            await SendMessageAsync(data);
                            Console.WriteLine("Сообщение с ошибкой об IP участника сети отправлено!!!");
                        }

                    } break;
                }
            }
            Console.WriteLine($"Клиент {_remoteEndPoint} отключился.");
            _stream.Close();
        }
        catch (IOException)
        {
            Console.WriteLine($"Подключение к {_remoteEndPoint} закрыто сервером.");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.GetType().Name + ": " + ex.Message);
        }
        if (!disposed)
            _disposeCallback(this);
    }

    public async Task SendMessageAsync(byte[] message)
    {
        //Console.WriteLine($">> {_remoteEndPoint}: {Encoding.UTF8.ToString(message)}");
        await _channel.Writer.WriteAsync(message);
    }

    private async Task RunWritingLoop()
    {
        byte[] header = new byte[4];
        await foreach(byte[] message in _channel.Reader.ReadAllAsync())
        {
            byte[] buffer = message;
            BinaryPrimitives.WriteInt32LittleEndian(header, buffer.Length);
            await _stream.WriteAsync(header, 0, header.Length);
            await _stream.WriteAsync(buffer, 0, buffer.Length);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposed)
            throw new ObjectDisposedException(GetType().FullName);
        disposed = true;
        if (_client.Connected)
        {
            _channel.Writer.Complete();
            _stream.Close();
            Task.WaitAll(_readingTask, _writingTask);
        }
        if (disposing)
        {
            _client.Dispose();
        }
    }

    ~Connection() => Dispose(false);
}