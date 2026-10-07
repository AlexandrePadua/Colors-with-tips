using System.Collections.Generic;
using Pitacor.Core.Protocol;

namespace Pitacor.Core.Match
{
    /// <summary>
    /// Outcome of one engine call. <see cref="Events"/> is the raw, unfiltered list for server-side use (logging,
    /// ReportFiled); clients must only ever receive <see cref="EventsFor"/>.
    /// </summary>
    public class EngineResult
    {
        private enum Audience { Everyone, ServerOnly, Restricted }

        private struct Routing
        {
            public Audience Audience;
            public HashSet<string> Privileged;
            public GameEvent Redacted;
        }

        private readonly List<Routing> _routing = new List<Routing>();

        /// <summary>Raw events in order. Contains secrets (e.g. hidden guess coordinates): never send as is.</summary>
        public List<GameEvent> Events { get; } = new List<GameEvent>();

        /// <summary>Rejections, each addressed to the player who attempted the action.</summary>
        public List<EngineError> Errors { get; } = new List<EngineError>();

        /// <summary>The room state (and Version) changed: views should be rebuilt.</summary>
        public bool StateChanged { get; set; }

        /// <summary>The events this player may receive, in order, with hidden data removed.</summary>
        public List<GameEvent> EventsFor(string playerId)
        {
            var list = new List<GameEvent>(Events.Count);
            for (int i = 0; i < Events.Count; i++)
            {
                var routing = _routing[i];
                switch (routing.Audience)
                {
                    case Audience.Everyone:
                        list.Add(Events[i]);
                        break;
                    case Audience.Restricted:
                        bool full = routing.Privileged == null || (playerId != null && routing.Privileged.Contains(playerId));
                        if (full) list.Add(Events[i]);
                        else if (routing.Redacted != null) list.Add(routing.Redacted);
                        break;
                }
            }
            return list;
        }

        internal void AddPublic(GameEvent e)
        {
            Events.Add(e);
            _routing.Add(new Routing { Audience = Audience.Everyone });
        }

        internal void AddServerOnly(GameEvent e)
        {
            Events.Add(e);
            _routing.Add(new Routing { Audience = Audience.ServerOnly });
        }

        /// <summary>Full event for the privileged ids (null = everyone), <paramref name="redacted"/> (or nothing) for the rest.</summary>
        internal void AddRestricted(GameEvent full, GameEvent redacted, HashSet<string> privileged)
        {
            Events.Add(full);
            _routing.Add(new Routing { Audience = Audience.Restricted, Privileged = privileged, Redacted = redacted });
        }

        internal void AddError(string playerId, string code, string detail)
        {
            Errors.Add(new EngineError { PlayerId = playerId, Code = code, Detail = detail });
        }
    }
}
