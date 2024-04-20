using System.Collections.Concurrent;
using System.Net;
using System.Collections.Generic;
using System.Net.Sockets;
using ipk24chat_server.Client;
using ipk24chat_server.Chat;

namespace ipk24chat_server.Udp;

public class UdpUser : AbstractChatUser
{
    private readonly UdpClient _udpClient;
    public static BlockingCollection<ConfirmMessage> ConfirmCollection = new BlockingCollection<ConfirmMessage>(10000);
    private ushort _lastSentMessageId = 0;
    private ushort _lastReceivedMessageId = 0;
    private readonly HashSet<ushort> _receivedMessageIds = new HashSet<ushort>(); // Tracks received message IDs to handle duplicates
    private readonly object _lock = new object();  // Lock object for synchronization
    private CancellationToken _cancellationToken;
    
    // Constructor
    public UdpUser(EndPoint endPoint, UdpClient udpClient, CancellationToken cancellationToken) : base(endPoint)
    {
        _udpClient = udpClient;
        _cancellationToken = cancellationToken;
    }

    public ushort LastReceivedMessageId
    {
        get
        {
            lock (_lock)
            {
                return _lastReceivedMessageId;
            }
        }
        set
        {
            lock (_lock)
            {
                _lastReceivedMessageId = value;
                _receivedMessageIds.Add(value);  // Add to received IDs set
            }
        }
    }

    public bool HasReceivedMessageId(ushort? messageId)
    {
        if (messageId != null)
        {
            lock (_lock)
            {
                return _receivedMessageIds.Contains((ushort)messageId);
            }
        }
        return false;

    }

    public override ushort? GetMessageIdToSend()
    {
        lock (_lock)
        {
            return _lastSentMessageId++;  // Ensure thread-safe increment and retrieval
        }
    }

    public override async Task SendMessageAsync(ClientMessage message)
    {
        if (!await SendMessageWithConfirmationAsync(message))
        {
            await ClientDisconnect(cancellationToken: _cancellationToken);
        }
    }

    private async Task<bool> SendMessageWithConfirmationAsync(ClientMessage message)
    {
        var currentMessageId = GetMessageIdToSend();
        byte[] dataToSend = UdpPacker.Pack(message, currentMessageId);
        int attempts = 0;

        while (attempts <= ChatSettings.RetransmissionCount)
        {
            try
            {
                // Assuming ConnectionEndPoint is an IPEndPoint or cast it appropriately
                IPEndPoint? destination = ConnectionEndPoint as IPEndPoint;
                if (destination == null)
                {
                    Console.WriteLine("Invalid endpoint type");
                    break;  // Exit loop if endpoint is not correctly specified
                }

                await _udpClient.SendAsync(dataToSend, dataToSend.Length, destination);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send message: {ex.Message}");
                await Task.Delay(ChatSettings.ConfirmationTimeout);
            }

            if (await WaitForConfirmation(currentMessageId))
            {
                return true;
            }

            attempts++;
        }

        return false;
    }


    private async Task<bool> WaitForConfirmation(ushort? messageId)
    {
        try
        {
            if (ConfirmCollection.TryTake(out var confirmMessage, TimeSpan.FromMilliseconds(ChatSettings.ConfirmationTimeout * 4)))
            {
                if (confirmMessage.MessageId == messageId)
                {
                    return true;
                }
            }
        }
        catch (Exception)
        {
            // Ignore exception, just return false
            return false;
        }
        return false;
    }
    public override Task ClientDisconnect(CancellationToken cancellationToken)
    {
        ChatUsers.RemoveUser(ConnectionEndPoint);
        ClientMessageQueue.TagUserMessages(this, "DISCONNECTED");  // Tag messages for cleanup
        
        if (DisplayName != string.Empty && ChannelId != string.Empty)
        {
            var leftChannelMessage = new MsgMessage("Server", $"{DisplayName} has left {ChannelId}");
            ChatMessagesQueue.Queue.Add(new ChatMessage(this, ChannelId, leftChannelMessage), cancellationToken);
        }
        
        return Task.CompletedTask;
    }
}