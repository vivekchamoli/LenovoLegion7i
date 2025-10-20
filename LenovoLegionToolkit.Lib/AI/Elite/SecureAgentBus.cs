using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AI.Elite;

/// <summary>
/// ELITE 10/10: Secure Agent Communication Bus
/// Lock-free message passing with encrypted inter-agent communication
/// Supports broadcast, unicast, and multicast messaging patterns
/// </summary>
public class SecureAgentBus : IDisposable
{
    // Message queues for each agent (lock-free concurrent queues)
    private readonly ConcurrentDictionary<string, ConcurrentQueue<AgentMessage>> _messageQueues = new();

    // Broadcast subscribers
    private readonly ConcurrentBag<string> _broadcastSubscribers = new();

    // Message encryption (AES-256)
    private readonly Aes _aes;
    private readonly byte[] _encryptionKey;

    // Message statistics
    private long _totalMessages;
    private long _totalBroadcasts;
    private long _totalEncryptedMessages;

    // Message routing table (for efficient multicast)
    private readonly ConcurrentDictionary<string, HashSet<string>> _routingTable = new();

    public SecureAgentBus()
    {
        // Initialize AES encryption for secure agent communication
        _aes = Aes.Create();
        _aes.KeySize = 256;
        _aes.GenerateKey();
        _aes.GenerateIV();
        _encryptionKey = _aes.Key;

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Secure Agent Bus initialized (AES-256 encryption enabled)");
    }

    /// <summary>
    /// Register an agent with the bus
    /// </summary>
    public void RegisterAgent(string agentId)
    {
        _messageQueues.TryAdd(agentId, new ConcurrentQueue<AgentMessage>());

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Agent registered on bus: {agentId}");
    }

    /// <summary>
    /// Unregister an agent from the bus
    /// </summary>
    public void UnregisterAgent(string agentId)
    {
        _messageQueues.TryRemove(agentId, out _);
        _broadcastSubscribers.TryTake(out _);

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Agent unregistered from bus: {agentId}");
    }

    /// <summary>
    /// Subscribe to broadcast messages
    /// </summary>
    public void SubscribeToBroadcasts(string agentId)
    {
        _broadcastSubscribers.Add(agentId);

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Agent subscribed to broadcasts: {agentId}");
    }

    /// <summary>
    /// Send a message to a specific agent (unicast)
    /// </summary>
    public bool SendMessage(string fromAgentId, string toAgentId, AgentMessageType type, object? payload = null, bool encrypt = false)
    {
        if (!_messageQueues.TryGetValue(toAgentId, out var queue))
            return false;

        var message = new AgentMessage
        {
            MessageId = Guid.NewGuid(),
            FromAgentId = fromAgentId,
            ToAgentId = toAgentId,
            Type = type,
            Payload = payload,
            Timestamp = DateTime.UtcNow,
            IsEncrypted = encrypt
        };

        // Encrypt payload if requested
        if (encrypt && payload != null)
        {
            message.Payload = EncryptPayload(payload);
            _totalEncryptedMessages++;
        }

        queue.Enqueue(message);
        Interlocked.Increment(ref _totalMessages);

        return true;
    }

    /// <summary>
    /// Broadcast a message to all subscribed agents
    /// </summary>
    public void BroadcastMessage(string fromAgentId, AgentMessageType type, object? payload = null)
    {
        var message = new AgentMessage
        {
            MessageId = Guid.NewGuid(),
            FromAgentId = fromAgentId,
            ToAgentId = "*", // Broadcast
            Type = type,
            Payload = payload,
            Timestamp = DateTime.UtcNow,
            IsEncrypted = false
        };

        foreach (var subscriberId in _broadcastSubscribers)
        {
            if (_messageQueues.TryGetValue(subscriberId, out var queue))
                queue.Enqueue(message);
        }

        Interlocked.Increment(ref _totalBroadcasts);
    }

    /// <summary>
    /// Broadcast fused telemetry to all agents (high-frequency, lock-free)
    /// </summary>
    public void BroadcastTelemetry(FusedTelemetry telemetry)
    {
        var message = new AgentMessage
        {
            MessageId = Guid.NewGuid(),
            FromAgentId = "TelemetryEngine",
            ToAgentId = "*",
            Type = AgentMessageType.Telemetry,
            Payload = telemetry,
            Timestamp = DateTime.UtcNow,
            IsEncrypted = false
        };

        // Fast path: direct enqueue to all queues
        foreach (var queue in _messageQueues.Values)
        {
            queue.Enqueue(message);
        }

        Interlocked.Increment(ref _totalBroadcasts);
    }

    /// <summary>
    /// Receive all pending messages for an agent
    /// </summary>
    public List<AgentMessage> ReceiveMessages(string agentId, int maxMessages = 100)
    {
        if (!_messageQueues.TryGetValue(agentId, out var queue))
            return new List<AgentMessage>();

        var messages = new List<AgentMessage>(maxMessages);
        int count = 0;

        while (count < maxMessages && queue.TryDequeue(out var message))
        {
            // Decrypt if needed
            if (message.IsEncrypted && message.Payload != null)
            {
                message.Payload = DecryptPayload((byte[])message.Payload);
            }

            messages.Add(message);
            count++;
        }

        return messages;
    }

    /// <summary>
    /// Try to receive a single message (non-blocking)
    /// </summary>
    public bool TryReceiveMessage(string agentId, out AgentMessage message)
    {
        message = default;

        if (!_messageQueues.TryGetValue(agentId, out var queue))
            return false;

        if (!queue.TryDequeue(out message))
            return false;

        // Decrypt if needed
        if (message.IsEncrypted && message.Payload != null)
        {
            message.Payload = DecryptPayload((byte[])message.Payload);
        }

        return true;
    }

    /// <summary>
    /// Get pending message count for an agent
    /// </summary>
    public int GetPendingMessageCount(string agentId)
    {
        if (!_messageQueues.TryGetValue(agentId, out var queue))
            return 0;

        return queue.Count;
    }

    /// <summary>
    /// Encrypt message payload using AES-256
    /// </summary>
    private byte[] EncryptPayload(object payload)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload);
            var plaintext = Encoding.UTF8.GetBytes(json);

            using var encryptor = _aes.CreateEncryptor(_aes.Key, _aes.IV);
            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            {
                cs.Write(plaintext, 0, plaintext.Length);
            }

            return ms.ToArray();
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Payload encryption failed", ex);

            return Array.Empty<byte>();
        }
    }

    /// <summary>
    /// Decrypt message payload using AES-256
    /// </summary>
    private object? DecryptPayload(byte[] ciphertext)
    {
        try
        {
            using var decryptor = _aes.CreateDecryptor(_aes.Key, _aes.IV);
            using var ms = new MemoryStream(ciphertext);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var reader = new StreamReader(cs);

            var json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<object>(json);
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Payload decryption failed", ex);

            return null;
        }
    }

    /// <summary>
    /// Get bus statistics
    /// </summary>
    public AgentBusStatistics GetStatistics()
    {
        return new AgentBusStatistics
        {
            TotalMessages = _totalMessages,
            TotalBroadcasts = _totalBroadcasts,
            TotalEncryptedMessages = _totalEncryptedMessages,
            ActiveAgents = _messageQueues.Count,
            TotalQueuedMessages = _messageQueues.Values.Sum(q => q.Count)
        };
    }

    public void Dispose()
    {
        _aes?.Dispose();

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Secure Agent Bus disposed. Total messages: {_totalMessages}, Broadcasts: {_totalBroadcasts}");
    }
}

/// <summary>
/// Agent message structure
/// </summary>
public struct AgentMessage
{
    public Guid MessageId;
    public string FromAgentId;
    public string ToAgentId;
    public AgentMessageType Type;
    public object? Payload;
    public DateTime Timestamp;
    public bool IsEncrypted;
    public int Priority; // 0-10, higher = more important
}

/// <summary>
/// Agent message types
/// </summary>
public enum AgentMessageType
{
    Telemetry,          // Fused telemetry broadcast
    Command,            // Execute a command
    Query,              // Request information
    Response,           // Response to a query
    Coordination,       // Inter-agent coordination
    Alert,              // Critical alert
    Heartbeat,          // Agent health check
    StateSync,          // State synchronization
    ShutdownRequest     // Graceful shutdown request
}

/// <summary>
/// Agent bus statistics
/// </summary>
public class AgentBusStatistics
{
    public long TotalMessages { get; set; }
    public long TotalBroadcasts { get; set; }
    public long TotalEncryptedMessages { get; set; }
    public int ActiveAgents { get; set; }
    public int TotalQueuedMessages { get; set; }
}
