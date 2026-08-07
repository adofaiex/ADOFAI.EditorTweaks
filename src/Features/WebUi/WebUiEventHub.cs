using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace ADOFAI.EditorTweaks.Features.WebUi
{
    internal sealed class WebUiEventHub
    {
        private readonly object gate = new object();
        private readonly List<Client> clients = new List<Client>();

        public Client AddClient()
        {
            Client client = new Client(this);
            lock (gate)
            {
                clients.Add(client);
            }

            return client;
        }

        public void RemoveClient(Client client)
        {
            lock (gate)
            {
                clients.Remove(client);
            }

            client.Dispose();
        }

        public void Publish(string eventName, string data)
        {
            string message = FormatEvent(eventName, data);
            Client[] snapshot;
            lock (gate)
            {
                snapshot = clients.ToArray();
            }

            foreach (Client client in snapshot)
            {
                if (!client.TryPublish(message))
                {
                    RemoveClient(client);
                }
            }
        }

        public void CloseAll()
        {
            Client[] snapshot;
            lock (gate)
            {
                snapshot = clients.ToArray();
                clients.Clear();
            }

            foreach (Client client in snapshot)
            {
                client.Dispose();
            }
        }

        public static string FormatEvent(string eventName, string data)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("event: ").Append(eventName).Append("\r\n");

            string normalized = (data ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
            string[] lines = normalized.Split(new[] { '\n' }, StringSplitOptions.None);
            foreach (string line in lines)
            {
                builder.Append("data: ").Append(line).Append("\r\n");
            }

            builder.Append("\r\n");
            return builder.ToString();
        }

        internal sealed class Client : IDisposable
        {
            private readonly WebUiEventHub owner;
            private readonly BlockingCollection<string> messages = new BlockingCollection<string>(new ConcurrentQueue<string>());

            public Client(WebUiEventHub owner)
            {
                this.owner = owner;
            }

            public bool TryTake(out string message, TimeSpan timeout)
            {
                try
                {
                    return messages.TryTake(out message!, (int)timeout.TotalMilliseconds);
                }
                catch (InvalidOperationException)
                {
                    message = string.Empty;
                    return false;
                }
            }

            public bool TryPublish(string message)
            {
                return !messages.IsAddingCompleted && messages.TryAdd(message);
            }

            public void Dispose()
            {
                messages.CompleteAdding();
                while (messages.TryTake(out _))
                {
                }
            }
        }
    }
}
