// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using osu.Game.Online.RealtimeMultiplayer;

namespace osu.Server.Spectator.Hubs
{
    [UsedImplicitly]
    [Authorize]
    public abstract class StatefulUserHub<TClient, TUserState> : Hub<TClient>
        where TUserState : ClientState
        where TClient : class
    {
        protected static readonly EntityStore<TUserState> ACTIVE_STATES = new EntityStore<TUserState>();

        protected StatefulUserHub(IDistributedCache cache)
        {
        }

        protected static KeyValuePair<long, TUserState?>[] GetAllStates() => ACTIVE_STATES.GetAllEntities();

        /// <summary>
        /// The osu! user id for the currently processing context.
        /// </summary>
        protected int CurrentContextUserId => int.Parse(Context.UserIdentifier);

        public override async Task OnConnectedAsync()
        {
            Console.WriteLine($"User {CurrentContextUserId} connected!");

            // if a previous connection is still present for the current user, we need to clean it up.
            await cleanUpState(false);

            await base.OnConnectedAsync();
        }

        public sealed override async Task OnDisconnectedAsync(Exception exception)
        {
            Console.WriteLine($"User {CurrentContextUserId} disconnected!");

            await cleanUpState(true);
        }

        private async Task cleanUpState(bool isDisconnect)
        {
            ItemUsage<TUserState>? usage;

            try
            {
                usage = await ACTIVE_STATES.GetForUse(CurrentContextUserId);
            }
            catch (ArgumentException)
            {
                // no state to clean up.
                return;
            }

            using (usage)
            {
                if (usage.Item != null)
                {
                    bool isOurState = usage.Item.ConnectionId != Context.ConnectionId;

                    if (isDisconnect && !isOurState)
                        // not our state, owned by a different connection.
                        return;
                }

                if (usage.Item != null)
                    await CleanUpState(usage.Item);

                usage.Destroy();
            }
        }

        /// <summary>
        /// Perform any cleanup required on the provided state.
        /// </summary>
        protected virtual Task CleanUpState(TUserState state) => Task.CompletedTask;

        protected async Task<ItemUsage<TUserState>> GetOrCreateLocalUserState()
        {
            var usage = await ACTIVE_STATES.GetForUse(CurrentContextUserId, true);

            if (usage.Item != null && usage.Item.ConnectionId != Context.ConnectionId)
            {
                usage.Dispose();
                throw new InvalidStateException("State is not valid for this connection");
            }

            return usage;
        }

        protected Task<ItemUsage<TUserState>> GetStateFromUser(int userId) =>
            ACTIVE_STATES.GetForUse(userId);

        public static string GetStateId(int userId) => $"state-{typeof(TClient)}:{userId}";

        public static void Reset() => ACTIVE_STATES.Clear();
    }
}
