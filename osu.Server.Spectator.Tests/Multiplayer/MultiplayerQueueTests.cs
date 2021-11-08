// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics;
using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets;
using osu.Server.Spectator.Database.Models;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    public class MultiplayerQueueTests : MultiplayerTest
    {
        [Fact]
        public async Task AddNonExistentBeatmapThrows()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(3333)).ReturnsAsync((string?)null);

            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.AddPlaylistItem(new APIPlaylistItem
            {
                BeatmapID = 3333,
                BeatmapChecksum = "checksum"
            }));
        }

        [Fact]
        public async Task AddCustomizedBeatmapThrows()
        {
            Database.Setup(d => d.GetBeatmapChecksumAsync(9999)).ReturnsAsync("correct checksum");

            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.AddPlaylistItem(new APIPlaylistItem
            {
                BeatmapID = 9999,
                BeatmapChecksum = "incorrect checksum",
            }));
        }

        [Theory]
        [InlineData(ILegacyRuleset.MAX_LEGACY_RULESET_ID + 1)]
        [InlineData(-1)]
        public async Task AddCustomRulesetThrows(int rulesetID)
        {
            await Hub.JoinRoom(ROOM_ID);
            await Assert.ThrowsAsync<InvalidStateException>(() => Hub.AddPlaylistItem(new APIPlaylistItem
            {
                BeatmapID = 1234,
                BeatmapChecksum = "checksum",
                RulesetID = rulesetID
            }));
        }

        [Fact]
        public async Task RoomStartsWithCurrentPlaylistItem()
        {
            await Hub.JoinRoom(ROOM_ID);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                var expectedPlaylistItem = await Database.Object.GetCandidatePlaylistItemByExpiry(ROOM_ID);
                Assert.Equal(expectedPlaylistItem.id, room.Settings.PlaylistItemId);
            }
        }

        [Fact]
        public async Task RoomHasNewPlaylistItemAfterMatchStartInHostOnlyMode()
        {
            long playlistItemId = (await Hub.JoinRoom(ROOM_ID)).Settings.PlaylistItemId;
            long expectedPlaylistItemId = playlistItemId + 1;

            Database.Setup(db => db.AddPlaylistItemAsync(It.IsAny<multiplayer_playlist_item>()))
                    .ReturnsAsync(() => expectedPlaylistItemId);

            await Hub.ChangeState(MultiplayerUserState.Ready);
            await Hub.StartMatch();
            await Hub.ChangeState(MultiplayerUserState.Loaded);
            await Hub.ChangeState(MultiplayerUserState.FinishedPlay);

            using (var usage = Hub.GetRoom(ROOM_ID))
            {
                var room = usage.Item;
                Debug.Assert(room != null);

                Assert.Equal(expectedPlaylistItemId, room.Settings.PlaylistItemId);
                Receiver.Verify(r => r.SettingsChanged(room.Settings), Times.Once);
            }
        }
    }
}
