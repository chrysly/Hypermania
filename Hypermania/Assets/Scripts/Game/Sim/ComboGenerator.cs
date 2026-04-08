using System;
using System.Collections.Generic;
using Design.Animation;
using Design.Configs;
using Netcode.Rollback;
using Utils;
using Utils.SoftFloat;

namespace Game.Sim
{
    public struct RhythmPattern
    {
        public AudioConfig.BeatSubdivision[] Beats;

        public static RhythmPattern Default =>
            new RhythmPattern
            {
                Beats = new[]
                {
                    AudioConfig.BeatSubdivision.EighthNote,
                    AudioConfig.BeatSubdivision.EighthNote,
                    AudioConfig.BeatSubdivision.QuarterNote,
                    AudioConfig.BeatSubdivision.QuarterNote,
                    AudioConfig.BeatSubdivision.QuarterNote,
                    AudioConfig.BeatSubdivision.QuarterNote,
                    AudioConfig.BeatSubdivision.QuarterNote,
                    AudioConfig.BeatSubdivision.EighthNote,
                    AudioConfig.BeatSubdivision.EighthNote,
                    AudioConfig.BeatSubdivision.QuarterNote,
                },
            };
    }

    public struct GeneratedComboMove
    {
        public InputFlags Input;
        public Frame BeatFrame;
        public bool IsMovement;
    }

    public struct GeneratedCombo
    {
        public List<GeneratedComboMove> Moves;
        public Frame EndFrame;
    }

    public static class ComboGenerator
    {
        private static readonly InputFlags[] AttackInputs =
        {
            InputFlags.LightAttack,
            InputFlags.MediumAttack,
            InputFlags.HeavyAttack,
            InputFlags.SpecialAttack,
        };

        private static InputFlags[] GetMovementInputs(FighterState attacker)
        {
            return new[] { InputFlags.Dash | attacker.ForwardInput, InputFlags.Up | attacker.ForwardInput };
        }

        /// <summary>
        /// Maximum frames to simulate when testing if a move hits.
        /// Should cover the longest attack animation (startup + active).
        /// </summary>
        private const int MAX_TEST_FRAMES = 40;

        /// <summary>
        /// Deterministic hash for random selection, derived from game state.
        /// </summary>
        private static int DeterministicHash(int realFrame, int beatIndex)
        {
            unchecked
            {
                int h = realFrame * 31 + beatIndex;
                h ^= h >> 16;
                h *= unchecked((int)0x45d9f3b);
                h ^= h >> 16;
                return h & 0x7FFFFFFF; // ensure non-negative
            }
        }

        private struct ValidCandidate
        {
            public InputFlags Input;
            public MoveTestResult Result;
        }

        private struct ValidMovementCandidate
        {
            public InputFlags MovementInput;
            public InputFlags AttackInput;
            public MoveTestResult Result;
        }

        public static GeneratedCombo Generate(
            GameState state,
            GameOptions options,
            int attackerIndex,
            RhythmPattern pattern,
            Frame firstBeatFrame
        )
        {
            GameOptions simOptions = new GameOptions
            {
                Global = options.Global,
                Players = options.Players,
                LocalPlayers = options.LocalPlayers,
                InfoOptions = options.InfoOptions,
                EnableMania = false,
                AlwaysRhythmCancel = true,
            };

            GameState simState = ComboValidationRunner.CloneState(state);
            simState.RoundEnd = Frame.Infinity;

            // Match real game: set correct mania alignment hitstop and ManiaStart mode
            // so the SpeedRatio curve (0.25 → 0.5 → 1.0) matches actual gameplay.
            // The clone inherits the original hit's hitstop, but the real game overwrites it
            // with the mania alignment value after StartRhythmCombo returns.
            int maniaHitstop = firstBeatFrame - simState.RealFrame - options.Global.ManiaSlowTicks;
            if (maniaHitstop < 0)
                maniaHitstop = 0;
            simState.HitstopFramesRemaining = maniaHitstop;
            simState.GameMode = GameMode.ManiaStart;
            simState.ModeStart = simState.RealFrame;

            // Advance through ManiaStart to first beat (DoManiaStart runs naturally with slow-mo)
            AdvanceStateTo(simState, simOptions, firstBeatFrame);

            // After ManiaStart, override for move testing
            simState.GameMode = GameMode.Fighting;
            simState.SpeedRatio = (sfloat)1f;

            List<GeneratedComboMove> moves = new List<GeneratedComboMove>();
            List<ValidCandidate> candidates = new List<ValidCandidate>();
            List<ValidCandidate> preferred = new List<ValidCandidate>();
            List<ValidMovementCandidate> movementCandidates = new List<ValidMovementCandidate>();
            List<ValidMovementCandidate> preferredMovement = new List<ValidMovementCandidate>();
            List<InputFlags> recentMoves = new List<InputFlags>();
            const int RECENCY_BUFFER_SIZE = 3;
            Frame currentBeat = firstBeatFrame;

            int beatCancelWindow = options.Global.Input.BeatCancelWindow;

            for (int i = 0; i < pattern.Beats.Length; i++)
            {
                if (i > 0)
                {
                    currentBeat = options.Global.Audio.NextBeat(currentBeat + 1, pattern.Beats[i]);
                }

                // Advance simulation state to this beat frame
                AdvanceStateTo(simState, simOptions, currentBeat);

                // Compute next beat for hitstop deadline
                bool hasNextBeat = i + 1 < pattern.Beats.Length;
                Frame nextBeat = hasNextBeat
                    ? options.Global.Audio.NextBeat(currentBeat + 1, pattern.Beats[i + 1])
                    : Frame.Infinity;

                // Collect all valid attack moves (standing and crouching)
                bool isLastBeat = !hasNextBeat;
                candidates.Clear();
                foreach (InputFlags attackInput in AttackInputs)
                {
                    TryAddCandidate(
                        candidates,
                        simState,
                        simOptions,
                        attackerIndex,
                        attackInput,
                        currentBeat,
                        nextBeat,
                        beatCancelWindow,
                        isLastBeat
                    );

                    // Also try crouching variant
                    TryAddCandidate(
                        candidates,
                        simState,
                        simOptions,
                        attackerIndex,
                        attackInput | InputFlags.Down,
                        currentBeat,
                        nextBeat,
                        beatCancelWindow,
                        isLastBeat
                    );
                }

                if (candidates.Count > 0)
                {
                    // Prefer moves not recently used
                    preferred.Clear();
                    foreach (var c in candidates)
                    {
                        if (!recentMoves.Contains(c.Input))
                            preferred.Add(c);
                    }

                    var pool = preferred.Count > 0 ? preferred : candidates;
                    int pick = DeterministicHash(state.RealFrame.No, i) % pool.Count;
                    ValidCandidate chosen = pool[pick];
                    moves.Add(
                        new GeneratedComboMove
                        {
                            Input = chosen.Input,
                            BeatFrame = currentBeat,
                            IsMovement = false,
                        }
                    );
                    simState = chosen.Result.ResultState;

                    if (recentMoves.Count >= RECENCY_BUFFER_SIZE)
                        recentMoves.RemoveAt(0);
                    recentMoves.Add(chosen.Input);
                    continue;
                }

                // No direct attack hits — try movement on this beat, then attack on next beat
                if (hasNextBeat)
                {
                    // The attack lands on nextBeat, so its deadline comes from beat i+2
                    bool hasNextNextBeat = i + 2 < pattern.Beats.Length;
                    Frame nextNextBeat = hasNextNextBeat
                        ? options.Global.Audio.NextBeat(nextBeat + 1, pattern.Beats[i + 2])
                        : Frame.Infinity;

                    bool isAttackLastBeat = !hasNextNextBeat;
                    movementCandidates.Clear();

                    foreach (InputFlags movementInput in GetMovementInputs(simState.Fighters[attackerIndex]))
                    {
                        GameState movedState = ComboValidationRunner.CloneState(simState);
                        ApplyInputAndAdvance(movedState, simOptions, attackerIndex, movementInput, nextBeat);

                        foreach (InputFlags attackInput in AttackInputs)
                        {
                            MoveTestResult result = ComboValidationRunner.TestMove(
                                movedState,
                                simOptions,
                                attackerIndex,
                                attackInput,
                                MAX_TEST_FRAMES
                            );
                            if (
                                result.Hit
                                && HitstopResolvesInTime(result, nextBeat, nextNextBeat, beatCancelWindow)
                                && (isAttackLastBeat || !IsMultiHit(simOptions, attackerIndex, result))
                            )
                            {
                                movementCandidates.Add(
                                    new ValidMovementCandidate
                                    {
                                        MovementInput = movementInput,
                                        AttackInput = attackInput,
                                        Result = result,
                                    }
                                );
                            }
                        }
                    }

                    if (movementCandidates.Count > 0)
                    {
                        // Prefer attack moves not recently used
                        preferredMovement.Clear();
                        foreach (var c in movementCandidates)
                        {
                            if (!recentMoves.Contains(c.AttackInput))
                                preferredMovement.Add(c);
                        }

                        var movPool = preferredMovement.Count > 0 ? preferredMovement : movementCandidates;
                        int pick = DeterministicHash(state.RealFrame.No, i) % movPool.Count;
                        ValidMovementCandidate chosen = movPool[pick];
                        moves.Add(
                            new GeneratedComboMove
                            {
                                Input = chosen.MovementInput,
                                BeatFrame = currentBeat,
                                IsMovement = true,
                            }
                        );
                        moves.Add(
                            new GeneratedComboMove
                            {
                                Input = chosen.AttackInput,
                                BeatFrame = nextBeat,
                                IsMovement = false,
                            }
                        );
                        simState = chosen.Result.ResultState;
                        currentBeat = nextBeat;
                        i++;

                        if (recentMoves.Count >= RECENCY_BUFFER_SIZE)
                            recentMoves.RemoveAt(0);
                        recentMoves.Add(chosen.AttackInput);
                        continue;
                    }
                }

                // Fallback: insert a forward dash note anyway
                InputFlags fallbackDash = InputFlags.Dash | simState.Fighters[attackerIndex].ForwardInput;
                moves.Add(
                    new GeneratedComboMove
                    {
                        Input = fallbackDash,
                        BeatFrame = currentBeat,
                        IsMovement = true,
                    }
                );
                ApplyInputAndAdvance(simState, simOptions, attackerIndex, fallbackDash, currentBeat);
            }

            Frame endFrame = options.Global.Audio.NextBeat(currentBeat + 1, AudioConfig.BeatSubdivision.QuarterNote);
            return new GeneratedCombo { Moves = moves, EndFrame = endFrame };
        }

        /// <summary>
        /// Returns true if the hitstop from a hit resolves before the next beat's cancel window opens.
        /// Advance() increments RealFrame before collision, so the hit's absolute frame = inputBeat + result.HitFrame + 1.
        /// Hitstop then freezes for HitstopTicks additional frames before the player is free.
        /// </summary>
        private static bool HitstopResolvesInTime(
            MoveTestResult result,
            Frame inputBeat,
            Frame deadlineBeat,
            int beatCancelWindow
        )
        {
            if (deadlineBeat == Frame.Infinity)
                return true;
            int absoluteHitFrame = inputBeat.No + result.HitFrame + 1;
            int hitstopResolvesAt = absoluteHitFrame + result.HitProps.HitstopTicks;
            return hitstopResolvesAt < deadlineBeat.No - beatCancelWindow;
        }

        /// <summary>
        /// Returns true if a move has multiple distinct hitbox BoxProps in its animation,
        /// meaning it can hit more than once (different props bypass immunity hash).
        /// </summary>
        private static bool IsMultiHit(GameOptions options, int attackerIndex, MoveTestResult result)
        {
            CharacterState moveState = result.ResultState.Fighters[attackerIndex].State;
            CharacterConfig config = options.Players[attackerIndex].Character;
            HitboxData data = config.GetHitboxData(moveState);

            BoxProps? first = null;
            for (int t = 0; t < data.TotalTicks; t++)
            {
                FrameData frame = data.GetFrame(t);
                if (frame.HasHitbox(out BoxProps props))
                {
                    if (!first.HasValue)
                    {
                        first = props;
                    }
                    else if (!props.Equals(first.Value))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void TryAddCandidate(
            List<ValidCandidate> candidates,
            GameState simState,
            GameOptions simOptions,
            int attackerIndex,
            InputFlags input,
            Frame currentBeat,
            Frame nextBeat,
            int beatCancelWindow,
            bool isLastBeat
        )
        {
            MoveTestResult result = ComboValidationRunner.TestMove(
                simState,
                simOptions,
                attackerIndex,
                input,
                MAX_TEST_FRAMES
            );
            if (
                result.Hit
                && HitstopResolvesInTime(result, currentBeat, nextBeat, beatCancelWindow)
                && (isLastBeat || !IsMultiHit(simOptions, attackerIndex, result))
            )
            {
                candidates.Add(new ValidCandidate { Input = input, Result = result });
            }
        }

        /// <summary>
        /// Advance a GameState forward by feeding empty inputs until its RealFrame reaches the target.
        /// </summary>
        private static void AdvanceStateTo(GameState state, GameOptions options, Frame targetRealFrame)
        {
            (GameInput input, InputStatus status)[] emptyInputs =
            {
                (GameInput.None, InputStatus.Confirmed),
                (GameInput.None, InputStatus.Confirmed),
            };

            while (state.RealFrame < targetRealFrame)
            {
                state.Advance(options, emptyInputs);
            }
        }

        /// <summary>
        /// Apply a single input on the attacker for one frame, then advance with empty inputs to the target frame.
        /// </summary>
        private static void ApplyInputAndAdvance(
            GameState state,
            GameOptions options,
            int attackerIndex,
            InputFlags input,
            Frame targetRealFrame
        )
        {
            // Apply the input for one frame
            (GameInput input, InputStatus status)[] inputs;
            if (attackerIndex == 0)
            {
                inputs = new (GameInput, InputStatus)[]
                {
                    (new GameInput(input), InputStatus.Confirmed),
                    (GameInput.None, InputStatus.Confirmed),
                };
            }
            else
            {
                inputs = new (GameInput, InputStatus)[]
                {
                    (GameInput.None, InputStatus.Confirmed),
                    (new GameInput(input), InputStatus.Confirmed),
                };
            }

            state.Advance(options, inputs);

            // Advance rest with empty inputs
            AdvanceStateTo(state, options, targetRealFrame);
        }
    }
}
