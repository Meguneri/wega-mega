using Content.IntegrationTests.Fixtures;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;
using ClientMediaPlayer = Content.Client.MediaPlayer.MediaPlayerSystem;
using ServerMediaPlayer = Content.Server.MediaPlayer.MediaPlayerSystem;

namespace Content.IntegrationTests.Tests._Wega;

[TestFixture]
[TestOf(typeof(ServerMediaPlayer))]
public sealed class MediaPlayerTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
    };

    [Test]
    public void SanitizeTitleStripsUnrenderableCharacters()
    {
        // CJK dropped, Latin + digits kept, runs collapsed to single spaces.
        Assert.That(ClientMediaPlayer.SanitizeTitle("TVアニメ「呪術廻戦」第2期OP"), Is.EqualTo("TV 2 OP"));
        // Cyrillic and Latin survive untouched.
        Assert.That(ClientMediaPlayer.SanitizeTitle("King Gnu — SPECIALZ"), Is.EqualTo("King Gnu — SPECIALZ"));
        Assert.That(ClientMediaPlayer.SanitizeTitle("Тест трек"), Is.EqualTo("Тест трек"));
        // Replacement chars from a bad decode are dropped too.
        Assert.That(ClientMediaPlayer.SanitizeTitle("TV��2�OP"), Is.EqualTo("TV 2 OP"));
    }

    /// <summary>
    /// Server broadcasts an ogg track; the connected client must assemble the chunks,
    /// load the audio and actually start playback.
    /// </summary>
    [Test]
    public async Task BroadcastTrackPlaysOnClient()
    {
        var server = Pair.Server;
        var client = Pair.Client;

        await server.WaitIdleAsync();
        await client.WaitIdleAsync();

        byte[] ogg = default!;
        await server.WaitPost(() =>
        {
            var res = server.ResolveDependency<IResourceManager>();
            using var stream = res.ContentFileRead(new ResPath("/Audio/Jukebox/sunset.ogg"));
            using var ms = new System.IO.MemoryStream();
            stream.CopyTo(ms);
            ogg = ms.ToArray();
        });

        Assert.That(ogg.Length, Is.GreaterThan(0), "Test track missing from resources");

        await server.WaitPost(() =>
        {
            var sys = server.System<ServerMediaPlayer>();
            sys.PlayData("test-track", "Тестовый трек", 60f, ogg);
        });

        await Pair.RunTicksSync(15);

        await client.WaitAssertion(() =>
        {
            var sys = client.System<ClientMediaPlayer>();

            Assert.That(sys.LastState, Is.Not.Null, "Client never received the play state");
            Assert.That(sys.LastState!.TrackId, Is.EqualTo("test-track"));
            Assert.That(sys.LastState.Title, Is.EqualTo("Тестовый трек"));
            Assert.That(sys.IsPlaying, Is.True, "Client did not start audio playback");
            Assert.That(sys.IsTrackPaused, Is.False);
        });

        // Pause: client stays loaded but reports paused.
        await server.WaitPost(() =>
        {
            var sys = server.System<ServerMediaPlayer>();
            sys.TogglePause();
        });

        await Pair.RunTicksSync(15);

        await client.WaitAssertion(() =>
        {
            var sys = client.System<ClientMediaPlayer>();
            Assert.That(sys.IsPlaying, Is.True, "Paused track should stay loaded");
            Assert.That(sys.IsTrackPaused, Is.True, "Client did not receive the paused state");
        });

        // Resume.
        await server.WaitPost(() =>
        {
            var sys = server.System<ServerMediaPlayer>();
            sys.TogglePause();
        });

        await Pair.RunTicksSync(15);

        await client.WaitAssertion(() =>
        {
            var sys = client.System<ClientMediaPlayer>();
            Assert.That(sys.IsTrackPaused, Is.False, "Client did not resume");
        });

        // Stop must reach the client too.
        await server.WaitPost(() =>
        {
            var sys = server.System<ServerMediaPlayer>();
            sys.Stop();
        });

        await Pair.RunTicksSync(15);

        await client.WaitAssertion(() =>
        {
            var sys = client.System<ClientMediaPlayer>();
            Assert.That(sys.IsPlaying, Is.False, "Client kept playing after server stop");
        });
    }
}
