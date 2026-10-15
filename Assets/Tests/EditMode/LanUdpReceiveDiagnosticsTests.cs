using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using NUnit.Framework;

namespace PocketBump.Tests
{
    public sealed class LanUdpReceiveDiagnosticsTests
    {
        [Test]
        public void IdleNonblockingPollsAreEmptyAndZeroLengthDatagramsAreStillObserved()
        {
            using (var receiver = new LanUdpPeer())
            using (var sender = new LanUdpPeer())
            {
                Assert.That(receiver.BindEphemeral() && sender.BindEphemeral(), Is.True);
                for (int i = 0; i < 4; i++) Assert.That(receiver.TryRecv(out _, out _), Is.False);
                Assert.That(receiver.ReceivePolls, Is.EqualTo(4));
                Assert.That(receiver.EmptyReceivePolls, Is.EqualTo(4));
                Assert.That(receiver.ReceiveFailures, Is.Zero);
                Assert.That(receiver.LastReceiveError, Is.Empty);
                var target = new IPEndPoint(IPAddress.Loopback, receiver.LocalEndPoint.Port);
                Assert.That(sender.SendTo(Array.Empty<byte>(), target), Is.True);
                byte[] received = Receive(receiver);
                Assert.That(received, Is.Empty);
                Assert.That(receiver.ReceivedDatagrams, Is.EqualTo(1));
                Assert.That(receiver.ReceiveFailures, Is.Zero);
            }
        }

        [Test]
        public void ReceiveSocketExceptionIsRetainedSeparatelyAndSubsequentRealDatagramRecovers()
        {
            using (var receiver = new LanUdpPeer())
            using (var sender = new LanUdpPeer())
            using (var invalidReceiveSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            using (var pendingConnection = new TcpClient(AddressFamily.InterNetwork))
            {
                Assert.That(receiver.BindEphemeral() && sender.BindEphemeral(), Is.True);
                var target = new IPEndPoint(IPAddress.Loopback, receiver.LocalEndPoint.Port);
                UdpClient client = Client(receiver);
                Socket original = client.Client;
                // A pending TCP connection makes this listener readable by Poll,
                // but ReceiveFrom on the listener produces a real SocketException.
                // The separately owned bound UDP socket is restored for recovery.
                invalidReceiveSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                invalidReceiveSocket.Listen(1);
                pendingConnection.Connect((IPEndPoint)invalidReceiveSocket.LocalEndPoint);
                invalidReceiveSocket.Blocking = false;
                Assert.That(invalidReceiveSocket.Poll(1000000, SelectMode.SelectRead), Is.True);
                try
                {
                    client.Client = invalidReceiveSocket;
                    Assert.That(receiver.TryRecv(out _, out _), Is.False);
                    Assert.That(receiver.ReceiveFailures, Is.EqualTo(1));
                    Assert.That(receiver.EmptyReceivePolls, Is.Zero);
                    Assert.That(receiver.LastReceiveError, Is.Not.Empty);
                    Assert.That(receiver.LastReceiveError, Does.Not.Contain("WouldBlock"));
                }
                finally { client.Client = original; }
                string error = receiver.LastReceiveError;
                Assert.That(sender.SendTo(new byte[] { 17, 23 }, target), Is.True);
                CollectionAssert.AreEqual(new byte[] { 17, 23 }, Receive(receiver));
                Assert.That(receiver.ReceiveFailures, Is.EqualTo(1));
                Assert.That(receiver.LastReceiveError, Is.EqualTo(error));
                Assert.That(receiver.SendFailures, Is.Zero);
                Assert.That(receiver.LastSocketError, Is.Empty);
            }
        }

        [Test]
        public void UnexpectedDisposedReceiveIsRecordedButIntentionalCloseIsQuiet()
        {
            using (var peer = new LanUdpPeer())
            {
                Assert.That(peer.BindEphemeral(), Is.True);
                Client(peer).Dispose();
                Assert.That(peer.TryRecv(out _, out _), Is.False);
                Assert.That(peer.ReceiveFailures, Is.EqualTo(1));
                Assert.That(peer.LastReceiveError, Is.EqualTo("SOCKET_DISPOSED"));
                Assert.That(peer.SendFailures, Is.Zero); Assert.That(peer.LastSocketError, Is.Empty);
                peer.Close();
                Assert.That(peer.TryRecv(out _, out _), Is.False);
                Assert.That(peer.ReceiveFailures, Is.EqualTo(1));
                Assert.That(peer.EmptyReceivePolls, Is.EqualTo(1));
                Assert.That(peer.LastReceiveError, Is.EqualTo("SOCKET_DISPOSED"));
            }
        }

        static UdpClient Client(LanUdpPeer peer) => (UdpClient)typeof(LanUdpPeer)
            .GetField("_udp", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(peer);

        static byte[] Receive(LanUdpPeer peer)
        {
            for (int i = 0; i < 100; i++)
            {
                if (peer.TryRecv(out byte[] data, out _)) return data;
                Thread.Sleep(1);
            }
            Assert.Fail("Local UDP datagram did not arrive."); return null;
        }
    }
}
