using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;

namespace cs2_rockthevote.Core
{
    public class TimeLimitManager : IPluginDependency<Plugin, Config>
    {
        private GameRules _gameRules;

        private ConVar? _timeLimit;

        public decimal TimeLimitValue {
            get
            {
                if (_timeLimit == null)
                {
                    LoadCvar();
                }
                if (_timeLimit != null)
                {
                    float value = _timeLimit.GetPrimitiveValue<float>();
                    return (decimal)value * 60M;
                }
                else
                {
                    return 0;
                }
            }
            set
            {
                if (_timeLimit == null)
                {
                    LoadCvar();
                }

                if (_timeLimit != null)
                {
                    _timeLimit.SetValue((float)(value / 60M));
                }
            }
        }

        public bool UnlimitedTime => TimeLimitValue <= 0;

        public decimal TimePlayed
        {
            get
            {
                if (_gameRules.WarmupRunning)
                    return 0;

                return (decimal)(Server.CurrentTime - _gameRules.GameStartTime);
            }
        }

        public decimal TimeRemaining
        {
            get
            {
                if (UnlimitedTime || TimePlayed > TimeLimitValue)
                    return 0;

                return TimeLimitValue - TimePlayed;
            }

            set
            {
                _timeLimit?.SetValue((float)value);
            }
        }

        public TimeLimitManager(GameRules gameRules)
        {
            _gameRules = gameRules;
        }

        void LoadCvar()
        {
            _timeLimit = ConVar.Find("mp_timelimit");
            if (_timeLimit == null)
            {
                Server.PrintToConsole("Unable to get the value for 'mp_timelimit'.");
            }
        }

        public void OnMapStart(string map)
        {
            LoadCvar();
        }

        public void OnLoad(Plugin plugin)
        {
            LoadCvar();
        }

        // Moved to ExtendRoundTimeManager.cs
       // public void ExtendTime(int minutes)
       // {
            // TODO: implement extending time
       // }
    }
}
