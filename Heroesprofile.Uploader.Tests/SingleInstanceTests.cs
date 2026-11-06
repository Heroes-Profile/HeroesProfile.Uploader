using System.Net.Sockets;
using Heroesprofile.Uploader.Desktop;
using Heroesprofile.Uploader.Desktop.Platform;
using Xunit;

namespace Heroesprofile.Uploader.Tests;

public class SingleInstanceTests
{
    [Fact]
    public void A_normal_socket_path_is_used_as_is()
    {
        var path = Path.Combine(Path.GetTempPath(), "hp", "instance.sock");
        Assert.Equal(path, SingleInstance.ShortEnoughForASocket(path));
    }

    [Fact]
    public void A_path_too_long_for_a_unix_socket_falls_back_to_a_short_one()
    {
        var deep = Path.Combine(Path.GetTempPath(), new string('x', 80), new string('y', 80), "instance.sock");

        var socket = SingleInstance.ShortEnoughForASocket(deep);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(socket) <= 104, socket); // macOS' limit, the tightest
        Assert.Equal(socket, SingleInstance.ShortEnoughForASocket(deep)); // stable across launches
    }

    [Fact]
    public void Different_long_paths_get_different_sockets()
    {
        var a = Path.Combine(Path.GetTempPath(), new string('a', 120), "instance.sock");
        var b = Path.Combine(Path.GetTempPath(), new string('b', 120), "instance.sock");

        Assert.NotEqual(SingleInstance.ShortEnoughForASocket(a), SingleInstance.ShortEnoughForASocket(b));
    }

    // Decides whether a launch may apply a staged update, which stops every running copy: a running
    // instance has to be seen, or a second launch kills it.
    [Fact]
    public void A_running_instance_is_seen_until_it_exits()
    {
        using (var instance = SingleInstance.TryAcquire(waitForPrevious: false)) {
            Assert.True(SingleInstance.IsRunning());
        }

        Assert.False(SingleInstance.IsRunning());
    }

    // The other way round: a socket file nobody listens on (what a crash leaves behind) isn't a
    // running instance, or staged updates would never install again.
    [Fact]
    public void A_socket_nobody_listens_on_is_not_a_running_instance()
    {
        var path = SingleInstance.ShortEnoughForASocket(Platforms.Current.SingleInstanceSocketPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Bound but not listening - disposing a socket deletes its file, so a real leftover can't be
        // made from here.
        using var leftover = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        leftover.Bind(new UnixDomainSocketEndPoint(path));

        Assert.True(File.Exists(path));
        Assert.False(SingleInstance.IsRunning());
    }
}
