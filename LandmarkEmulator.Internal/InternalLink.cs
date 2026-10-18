using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace LandmarkEmulator.Internal
{
    /// <summary>
    /// One TCP connection between two services carrying internal packets, each framed as u32 length + packet
    /// (internal-transport.md §1). Sends are queued and written in order by one task; received packets are parsed and
    /// handed to <see cref="Received"/> on the reading task, in order. Closing (either side, or an error) raises
    /// <see cref="Closed"/> once.
    /// </summary>
    public sealed class InternalLink : IDisposable
    {
        /// <summary>The largest frame accepted (a design voxel reply is the biggest expected packet).</summary>
        public const int MaxFrame = 16 * 1024 * 1024;

        private readonly TcpClient client;
        private readonly NetworkStream stream;
        private readonly Channel<byte[]> outgoing = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
        private readonly CancellationTokenSource stop = new();
        private int closed;

        /// <summary>Where the other side is (for logs).</summary>
        public EndPoint Remote { get; }

        /// <summary>A parsed packet arrived. Runs on the link's reading task: keep it short or hand the work over.</summary>
        public event Action<InternalLink, IInternalMessage> Received;
        /// <summary>The link closed (with the error, or null for a clean close). Raised once.</summary>
        public event Action<InternalLink, Exception> Closed;

        /// <summary>
        /// A <see cref="Received"/> handler threw (the message is dropped, the link keeps reading). For the owner's log;
        /// without a subscriber it goes to standard error.
        /// </summary>
        public static event Action<InternalLink, IInternalMessage, Exception> HandlerFailed;

        /// <summary>Anything the owner wants to keep with the link (e.g. the back end it registered as).</summary>
        public object Tag { get; set; }

        public bool IsOpen => Volatile.Read(ref closed) == 0;

        public InternalLink(TcpClient client)
        {
            this.client = client;
            client.NoDelay = true;
            stream = client.GetStream();
            Remote = client.Client.RemoteEndPoint;
        }

        /// <summary>Starts reading and writing. Call after subscribing to the events.</summary>
        public void Start()
        {
            _ = Task.Run(ReadLoop);
            _ = Task.Run(WriteLoop);
        }

        /// <summary>Queues a packet; false when the link is closed.</summary>
        public bool Send(IInternalMessage message) => SendRaw(InternalMessages.Serialise(message));

        /// <summary>Queues an already serialised packet (opcode byte first).</summary>
        public bool SendRaw(byte[] packet)
        {
            if (!IsOpen)
                return false;
            if (packet.Length > MaxFrame)
                throw new ArgumentException($"Internal packet of {packet.Length} bytes is over the {MaxFrame} limit.");
            return outgoing.Writer.TryWrite(packet);
        }

        private async Task WriteLoop()
        {
            var header = new byte[4];
            try
            {
                await foreach (byte[] packet in outgoing.Reader.ReadAllAsync(stop.Token))
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)packet.Length);
                    await stream.WriteAsync(header, stop.Token);
                    await stream.WriteAsync(packet, stop.Token);
                }
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
            {
                Close(e is OperationCanceledException ? null : e);
            }
        }

        private async Task ReadLoop()
        {
            var header = new byte[4];
            try
            {
                while (true)
                {
                    await stream.ReadExactlyAsync(header, stop.Token);
                    uint length = BinaryPrimitives.ReadUInt32LittleEndian(header);
                    if (length == 0 || length > MaxFrame)
                        throw new InvalidDataException($"Internal frame of {length} bytes from {Remote}.");
                    var packet = new byte[length];
                    await stream.ReadExactlyAsync(packet, stop.Token);
                    IInternalMessage message;
                    try
                    {
                        message = InternalMessages.Parse(packet);
                    }
                    catch (Exception e) when (e is not InvalidDataException and not OutOfMemoryException)
                    {
                        // Any other parse failure is a bad frame too (closed below, like InvalidDataException).
                        throw new InvalidDataException($"Unreadable internal packet 0x{packet[0]:X2} from {Remote}: {e.Message}", e);
                    }
                    // A handler that throws must not end the reading: the exception used to escape this loop's catch
                    // filter, the task faulted unobserved and the link stayed "open" without ever reading again — no
                    // Closed, so no reconnect and no hold, while every later packet from the other side was ignored
                    // (a gateway whose zone link stopped this way left all its players frozen). The message is dropped.
                    try
                    {
                        Received?.Invoke(this, message);
                    }
                    catch (Exception e) when (e is not OutOfMemoryException)
                    {
                        if (HandlerFailed is { } report)
                            report(this, message, e);
                        else
                            Console.Error.WriteLine($"Internal link {Remote}: handler for {message.Opcode} failed: {e}");
                    }
                }
            }
            catch (EndOfStreamException)
            {
                Close(null);
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or OperationCanceledException
                                          or InvalidDataException)
            {
                Close(e is OperationCanceledException ? null : e);
            }
        }

        /// <summary>Closes the link (queued packets not yet written are dropped).</summary>
        public void Close(Exception error = null)
        {
            if (Interlocked.Exchange(ref closed, 1) != 0)
                return;
            outgoing.Writer.TryComplete();
            stop.Cancel();
            try
            {
                client.Close();
            }
            catch (SocketException)
            {
            }
            Closed?.Invoke(this, error);
        }

        public void Dispose() => Close();
    }

    /// <summary>The hub side (e.g. the gateway): accepts links from services that dial it.</summary>
    public sealed class InternalListener : IDisposable
    {
        private readonly TcpListener listener;
        private readonly CancellationTokenSource stop = new();

        /// <summary>A service connected. Subscribe to the link's events here, then call <see cref="InternalLink.Start"/>.</summary>
        public event Action<InternalLink> Accepted;

        public InternalListener(IPAddress address, int port) => listener = new TcpListener(address, port);

        /// <summary>The port it listens on (useful with port 0).</summary>
        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        public void Start()
        {
            listener.Start();
            _ = Task.Run(AcceptLoop);
        }

        private async Task AcceptLoop()
        {
            while (!stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(stop.Token);
                }
                catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
                {
                    return;
                }
                Accepted?.Invoke(new InternalLink(client));
            }
        }

        public void Dispose()
        {
            stop.Cancel();
            listener.Stop();
        }
    }

    /// <summary>
    /// The dialling side (a back end to the gateway, a zone to a shared service): connects to "host:port" and connects
    /// again after a loss, waiting longer each time up to <see cref="MaxDelay"/>, so a service restarting at the other end
    /// is picked up by itself.
    /// </summary>
    public sealed class InternalDialer : IDisposable
    {
        private readonly string host;
        private readonly int port;
        private readonly CancellationTokenSource stop = new();

        public static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(1), MaxDelay = TimeSpan.FromSeconds(15);

        /// <summary>The current link, or null while not connected.</summary>
        public InternalLink Link { get; private set; }

        /// <summary>A new link is up: subscribe to its events and send any registration; it is started afterwards.</summary>
        public event Action<InternalLink> Connected;

        /// <param name="address">"host:port", e.g. from .env (SOCIAL_ADDRESS=social:20200).</param>
        public InternalDialer(string address)
        {
            int colon = address?.LastIndexOf(':') ?? -1;
            if (colon <= 0 || !int.TryParse(address[(colon + 1)..], out port))
                throw new ArgumentException($"\"{address}\" is not host:port.");
            host = address[..colon];
        }

        public void Start() => _ = Task.Run(DialLoop);

        /// <summary>Sends when connected; false when not (the caller decides whether to queue, refuse or drop).</summary>
        public bool Send(IInternalMessage message) => Link?.Send(message) ?? false;

        private async Task DialLoop()
        {
            TimeSpan delay = FirstDelay;
            while (!stop.IsCancellationRequested)
            {
                var client = new TcpClient();
                try
                {
                    await client.ConnectAsync(host, port, stop.Token);
                }
                catch (Exception e) when (e is SocketException or OperationCanceledException)
                {
                    client.Dispose();
                    if (stop.IsCancellationRequested)
                        return;
                    await Task.Delay(delay, stop.Token).ContinueWith(_ => { });
                    delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxDelay.Ticks));
                    continue;
                }
                delay = FirstDelay;
                var link = new InternalLink(client);
                var down = new TaskCompletionSource();
                link.Closed += (_, _) => down.TrySetResult();
                Link = link;
                Connected?.Invoke(link);
                link.Start();
                await down.Task;
                Link = null;
            }
        }

        public void Dispose()
        {
            stop.Cancel();
            Link?.Close();
        }
    }
}
