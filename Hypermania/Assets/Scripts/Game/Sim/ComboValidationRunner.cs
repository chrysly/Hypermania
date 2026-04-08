using System.Buffers;
using Design.Animation;
using MemoryPack;
using Netcode.Rollback;
using Utils.SoftFloat;

namespace Game.Sim
{
    public struct MoveTestResult
    {
        public bool Hit;
        public int HitFrame;
        public BoxProps HitProps;
        public GameState ResultState;
    }

    public static class ComboValidationRunner
    {
        [System.ThreadStatic]
        private static ArrayBufferWriter<byte> _cloneWriter;

        private static ArrayBufferWriter<byte> CloneWriter
        {
            get
            {
                if (_cloneWriter == null)
                    _cloneWriter = new ArrayBufferWriter<byte>(4096);
                return _cloneWriter;
            }
        }

        public static GameState CloneState(GameState state)
        {
            CloneWriter.Clear();
            MemoryPackSerializer.Serialize(CloneWriter, state);
            return MemoryPackSerializer.Deserialize<GameState>(CloneWriter.WrittenSpan.ToArray());
        }

        public static MoveTestResult TestMove(
            GameState state,
            GameOptions options,
            int attackerIndex,
            InputFlags move,
            int maxFrames
        )
        {
            GameState clone = CloneState(state);
            clone.GameMode = GameMode.Fighting;
            clone.RoundEnd = Utils.Frame.Infinity;

            int defenderIndex = 1 - attackerIndex;

            // Only enable rhythm cancel on the input frame (frame 0).
            // In real mania, rhythm cancel fires on the exact note frame only.
            // If AlwaysRhythmCancel stays true on subsequent frames, ApplyActiveState
            // bypasses the Actionable check and can override the attack with a different
            // variant (e.g., crouching attack overridden by standing attack when Down is no longer held).
            bool savedRhythmCancel = options.AlwaysRhythmCancel;

            for (int frame = 0; frame < maxFrames; frame++)
            {
                InputFlags attackerFlags = frame == 0 ? move : InputFlags.None;
                options.AlwaysRhythmCancel = frame == 0 && savedRhythmCancel;

                (GameInput input, InputStatus status)[] inputs;
                if (attackerIndex == 0)
                {
                    inputs = new (GameInput, InputStatus)[]
                    {
                        (new GameInput(attackerFlags), InputStatus.Confirmed),
                        (GameInput.None, InputStatus.Confirmed),
                    };
                }
                else
                {
                    inputs = new (GameInput, InputStatus)[]
                    {
                        (GameInput.None, InputStatus.Confirmed),
                        (new GameInput(attackerFlags), InputStatus.Confirmed),
                    };
                }

                clone.Advance(options, inputs);

                if (clone.Fighters[defenderIndex].HitProps.HasValue)
                {
                    return new MoveTestResult
                    {
                        Hit = true,
                        HitFrame = frame,
                        HitProps = clone.Fighters[defenderIndex].HitProps.Value,
                        ResultState = clone,
                    };
                }
            }

            options.AlwaysRhythmCancel = savedRhythmCancel;
            return new MoveTestResult { Hit = false, ResultState = clone };
        }
    }
}
