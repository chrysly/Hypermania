using Design.Configs;
using Utils;

namespace Game.Sim
{
    public struct RhythmComboManager
    {
        /// <summary>
        /// Generates a dynamic combo using simulation and queues the resulting notes.
        /// Returns the number of hitstop frames to apply.
        /// </summary>
        public int StartRhythmCombo(
            Frame realFrame,
            ref ManiaState state,
            FighterFacing facingDir,
            GameOptions options,
            CharacterConfig characterConfig,
            GameState gameState,
            int attackerIndex
        )
        {
            // Find the first beat after the mania slow-motion startup
            Frame nextBeat = realFrame;
            while (nextBeat - realFrame < options.Global.ManiaSlowTicks)
            {
                nextBeat = options.Global.Audio.NextBeat(nextBeat + 1, AudioConfig.BeatSubdivision.QuarterNote);
            }

            int hitstop = nextBeat - (realFrame + options.Global.ManiaSlowTicks);

            // Generate combo dynamically via simulation
            RhythmPattern pattern = RhythmPattern.Default;
            GeneratedCombo combo = ComboGenerator.Generate(gameState, options, attackerIndex, pattern, nextBeat);

            // Queue notes to mania channels
            for (int i = 0; i < combo.Moves.Count; i++)
            {
                InputFlags hitInput = combo.Moves[i].Input;
                if (facingDir == FighterFacing.Left)
                {
                    hitInput = GameInput.FlipHorizontalInputs(hitInput);
                }

                state.QueueNote(
                    i % 4,
                    new ManiaNote
                    {
                        Length = 0,
                        Tick = combo.Moves[i].BeatFrame,
                        HitInput = hitInput,
                    }
                );
            }

            state.Enable(combo.EndFrame);
            return hitstop;
        }
    }
}
