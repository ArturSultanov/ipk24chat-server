using ipk24chat_server.Client;

namespace ipk24chat_server.System;

/*
 * Logger is a class that is used to log all incoming and outgoing messages.
 * It is used to log the direction of the message, the endpoint, and the type of message.
 */
public static class Logger
{
    public static void LogIo(string direction, string? endPoint, ClientMessage message)
    {
        string type = message switch
        {
            AuthMessage => "Auth",
            JoinMessage => "Join",
            MsgMessage => "Msg",
            ConfirmMessage => "Confirm",
            _ => "Unknown"
        };
        Console.WriteLine($"{direction} {endPoint} | {type}");
    }
}