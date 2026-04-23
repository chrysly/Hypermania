using System.Collections.Generic;
using System.Text;
using Design.Configs;
using Game;
using UnityEditor;
using UnityEngine;
using Utils.EnumArray;

namespace Design.Animation.FrameDataWindow.Editor
{
    public sealed class FrameDataWindow : EditorWindow
    {
        [SerializeField]
        private CharacterConfig _config;

        private Vector2 _scroll;

        private const float RowHeight = 20f;
        private const float HeaderHeight = 22f;
        private const float CellPadX = 4f;

        private static readonly Color RowBgOdd = new Color(1f, 1f, 1f, 0.04f);
        private static readonly Color HeaderBg = new Color(0f, 0f, 0f, 0.25f);
        private static readonly Color BorderColor = new Color(0f, 0f, 0f, 0.5f);

        private static readonly string[] MoveHeaders =
        {
            "State",
            "Attack",
            "Kind",
            "Dmg",
            "Startup",
            "Active",
            "Recovery",
            "Hitstun",
            "Blockstun",
            "On Hit",
            "On Block",
            "KD",
            "Unbl",
            "Tech",
            "Gatlings",
        };

        // Last entry is flex — grows to fill remaining row width.
        private static readonly float[] MoveWidths =
        {
            160f, // State
            55f, // Attack
            55f, // Kind
            40f, // Dmg
            55f, // Startup
            50f, // Active
            60f, // Recovery
            55f, // Hitstun
            65f, // Blockstun
            55f, // On Hit
            65f, // On Block
            50f, // KD
            40f, // Unbl
            40f, // Tech
            260f, // Gatlings (minimum)
        };

        private static readonly string[] ProjectileHeaders =
        {
            "Trigger State",
            "Spawn",
            "Lifetime",
            "Attack",
            "Kind",
            "Dmg",
            "Hitstun",
            "Blockstun",
            "On Hit",
            "On Block",
            "KD",
            "Unbl",
        };

        private static readonly float[] ProjectileWidths =
        {
            180f,
            55f,
            65f,
            55f,
            55f,
            40f,
            55f,
            65f,
            55f,
            65f,
            50f,
            40f,
        };

        [MenuItem("Hypermania/Frame Data Viewer")]
        public static void ShowWindow()
        {
            var window = GetWindow<FrameDataWindow>("Frame Data");
            window.minSize = new Vector2(1200f, 420f);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            _config = (CharacterConfig)EditorGUILayout.ObjectField(
                "Character Config",
                _config,
                typeof(CharacterConfig),
                false
            );

            if (_config == null)
            {
                EditorGUILayout.HelpBox("Assign a CharacterConfig asset to view its frame data.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawMovesTable(_config);
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            DrawProjectilesTable(_config);

            EditorGUILayout.EndScrollView();
        }

        // ─────────── Moves ───────────

        private static void DrawMovesTable(CharacterConfig config)
        {
            EditorGUILayout.LabelField("Moves", EditorStyles.boldLabel);

            float[] widths = WithFlexLast(MoveWidths);
            DrawHeaderRow(MoveHeaders, widths);

            int rowIndex = 0;
            CharacterState[] keys = EnumIndexCache<CharacterState>.Keys;
            for (int i = 0; i < keys.Length; i++)
            {
                CharacterState state = keys[i];
                HitboxData data = config.Hitboxes != null ? config.Hitboxes[state] : null;
                if (data == null)
                    continue;
                if (!data.HasHitbox())
                    continue;

                List<BoxProps> uniqueBoxes = CollectUniqueHitboxes(data);
                if (uniqueBoxes.Count == 0)
                    continue;

                int refIdx = FindLatestUniqueBoxIndex(uniqueBoxes, data);
                int refFirstFrame = refIdx >= 0 ? FindFirstFrameOfLastContiguousRun(data, uniqueBoxes[refIdx]) : -1;

                string gatlings = GatlingsFor(config, state);

                for (int b = 0; b < uniqueBoxes.Count; b++)
                {
                    BoxProps props = uniqueBoxes[b];
                    bool isFirst = b == 0;
                    bool isReference = b == refIdx;
                    string onHitStr = "";
                    string onBlockStr = "";
                    if (isReference && refFirstFrame >= 0)
                    {
                        onHitStr = FormatOnHit(props, data.TotalTicks - refFirstFrame);
                        onBlockStr = FormatAdvantage(props.BlockstunTicks - (data.TotalTicks - refFirstFrame));
                    }

                    DrawMoveRow(
                        rowIndex: rowIndex,
                        widths: widths,
                        state: state,
                        data: data,
                        props: props,
                        isFirstRowOfMove: isFirst,
                        onHitStr: onHitStr,
                        onBlockStr: onBlockStr,
                        gatlings: isFirst ? gatlings : ""
                    );
                    rowIndex++;
                }
            }
        }

        private static void DrawMoveRow(
            int rowIndex,
            float[] widths,
            CharacterState state,
            HitboxData data,
            BoxProps props,
            bool isFirstRowOfMove,
            string onHitStr,
            string onBlockStr,
            string gatlings
        )
        {
            Rect row = ReserveRow(RowHeight);
            DrawRowDecorations(row, rowIndex, widths, isHeader: false);

            int c = 0;
            if (isFirstRowOfMove)
            {
                if (GUI.Button(CellRect(row, widths, c), state.ToString(), EditorStyles.label))
                {
                    EditorGUIUtility.PingObject(data);
                    Selection.activeObject = data;
                }
            }
            c++;

            GUI.Label(CellRect(row, widths, c++), AttackKindLabel(props));
            GUI.Label(CellRect(row, widths, c++), KindLabel(props.Kind));
            GUI.Label(CellRect(row, widths, c++), props.Damage.ToString());

            GUI.Label(CellRect(row, widths, c++), isFirstRowOfMove ? data.StartupTicks.ToString() : "");
            GUI.Label(CellRect(row, widths, c++), isFirstRowOfMove ? data.ActiveTicks.ToString() : "");
            GUI.Label(CellRect(row, widths, c++), isFirstRowOfMove ? data.RecoveryTicks.ToString() : "");

            GUI.Label(CellRect(row, widths, c++), props.HitstunTicks.ToString());
            GUI.Label(CellRect(row, widths, c++), props.BlockstunTicks.ToString());
            GUI.Label(CellRect(row, widths, c++), onHitStr);
            GUI.Label(CellRect(row, widths, c++), onBlockStr);
            GUI.Label(CellRect(row, widths, c++), KnockdownLabel(props.KnockdownKind));
            GUI.Label(CellRect(row, widths, c++), props.Unblockable ? "Y" : "");
            GUI.Label(CellRect(row, widths, c++), props.Kind == HitboxKind.Grabbox ? (props.Techable ? "Y" : "N") : "");
            GUI.Label(CellRect(row, widths, c++), gatlings);
        }

        // ─────────── Projectiles ───────────

        private static void DrawProjectilesTable(CharacterConfig config)
        {
            EditorGUILayout.LabelField("Projectiles", EditorStyles.boldLabel);

            if (config.Projectiles == null || config.Projectiles.Count == 0)
            {
                EditorGUILayout.LabelField("(none)");
                return;
            }

            float[] widths = ProjectileWidths;
            DrawHeaderRow(ProjectileHeaders, widths);

            int rowIndex = 0;
            for (int i = 0; i < config.Projectiles.Count; i++)
            {
                ProjectileConfig p = config.Projectiles[i];
                if (p == null)
                    continue;

                rowIndex += DrawProjectileBlock(p, widths, rowIndex, p.HitboxData, isOnDeath: false);

                if (p.HasOnDeath && p.OnDeathHitbox != null)
                {
                    rowIndex += DrawProjectileBlock(p, widths, rowIndex, p.OnDeathHitbox, isOnDeath: true);
                }
            }
        }

        private static int DrawProjectileBlock(
            ProjectileConfig p,
            float[] widths,
            int startingRowIndex,
            HitboxData hitbox,
            bool isOnDeath
        )
        {
            if (hitbox == null)
            {
                DrawProjectileRow(
                    startingRowIndex,
                    widths,
                    p,
                    isHeaderOfBlock: true,
                    label: isOnDeath ? "    ↳ on-death" : null,
                    missingHitbox: true,
                    props: default,
                    onHitStr: "",
                    onBlockStr: ""
                );
                return 1;
            }

            List<BoxProps> boxes = CollectUniqueHitboxes(hitbox);
            if (boxes.Count == 0)
            {
                DrawProjectileRow(
                    startingRowIndex,
                    widths,
                    p,
                    isHeaderOfBlock: true,
                    label: isOnDeath ? "    ↳ on-death" : null,
                    missingHitbox: true,
                    props: default,
                    onHitStr: "",
                    onBlockStr: ""
                );
                return 1;
            }

            int refIdx = FindLatestUniqueBoxIndex(boxes, hitbox);
            int refFirstFrame = refIdx >= 0 ? FindFirstFrameOfLastContiguousRun(hitbox, boxes[refIdx]) : -1;

            for (int b = 0; b < boxes.Count; b++)
            {
                BoxProps props = boxes[b];
                bool isReference = b == refIdx;
                string onHitStr = "";
                string onBlockStr = "";
                if (isReference && refFirstFrame >= 0)
                {
                    onHitStr = FormatOnHit(props, hitbox.TotalTicks - refFirstFrame);
                    onBlockStr = FormatAdvantage(props.BlockstunTicks - (hitbox.TotalTicks - refFirstFrame));
                }

                DrawProjectileRow(
                    startingRowIndex + b,
                    widths,
                    p,
                    isHeaderOfBlock: b == 0,
                    label: isOnDeath ? (b == 0 ? "    ↳ on-death" : "") : null,
                    missingHitbox: false,
                    props: props,
                    onHitStr: onHitStr,
                    onBlockStr: onBlockStr
                );
            }
            return boxes.Count;
        }

        private static void DrawProjectileRow(
            int rowIndex,
            float[] widths,
            ProjectileConfig p,
            bool isHeaderOfBlock,
            string label,
            bool missingHitbox,
            BoxProps props,
            string onHitStr,
            string onBlockStr
        )
        {
            Rect row = ReserveRow(RowHeight);
            DrawRowDecorations(row, rowIndex, widths, isHeader: false);

            int c = 0;
            if (label != null)
            {
                GUI.Label(CellRect(row, widths, c), label);
            }
            else if (isHeaderOfBlock)
            {
                if (GUI.Button(CellRect(row, widths, c), p.TriggerState.ToString(), EditorStyles.label))
                {
                    EditorGUIUtility.PingObject(p);
                    Selection.activeObject = p;
                }
            }
            c++;

            GUI.Label(CellRect(row, widths, c++), isHeaderOfBlock && label == null ? p.SpawnTick.ToString() : "");
            GUI.Label(CellRect(row, widths, c++), isHeaderOfBlock && label == null ? p.LifetimeTicks.ToString() : "");

            if (missingHitbox)
            {
                GUI.Label(CellRect(row, widths, c++), "(no hitbox)");
                for (; c < widths.Length; c++)
                    GUI.Label(CellRect(row, widths, c), "");
                return;
            }

            GUI.Label(CellRect(row, widths, c++), AttackKindLabel(props));
            GUI.Label(CellRect(row, widths, c++), KindLabel(props.Kind));
            GUI.Label(CellRect(row, widths, c++), props.Damage.ToString());
            GUI.Label(CellRect(row, widths, c++), props.HitstunTicks.ToString());
            GUI.Label(CellRect(row, widths, c++), props.BlockstunTicks.ToString());
            GUI.Label(CellRect(row, widths, c++), onHitStr);
            GUI.Label(CellRect(row, widths, c++), onBlockStr);
            GUI.Label(CellRect(row, widths, c++), KnockdownLabel(props.KnockdownKind));
            GUI.Label(CellRect(row, widths, c++), props.Unblockable ? "Y" : "");
        }

        // ─────────── Table rendering helpers ───────────

        private static float[] WithFlexLast(float[] baseWidths)
        {
            float[] effective = (float[])baseWidths.Clone();
            float available = EditorGUIUtility.currentViewWidth - 30f;
            float sumFixed = 0f;
            for (int i = 0; i < effective.Length - 1; i++)
                sumFixed += effective[i];
            float lastMin = effective[effective.Length - 1];
            effective[effective.Length - 1] = Mathf.Max(lastMin, available - sumFixed);
            return effective;
        }

        private static void DrawHeaderRow(string[] headers, float[] widths)
        {
            Rect row = ReserveRow(HeaderHeight);
            DrawRowDecorations(row, 0, widths, isHeader: true);
            for (int i = 0; i < headers.Length; i++)
            {
                GUI.Label(CellRect(row, widths, i), headers[i], EditorStyles.boldLabel);
            }
        }

        private static Rect ReserveRow(float height)
        {
            return GUILayoutUtility.GetRect(0f, height, GUILayout.ExpandWidth(true));
        }

        private static void DrawRowDecorations(Rect row, int rowIndex, float[] widths, bool isHeader)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            if (isHeader)
            {
                EditorGUI.DrawRect(row, HeaderBg);
            }
            else if ((rowIndex & 1) == 1)
            {
                EditorGUI.DrawRect(row, RowBgOdd);
            }

            // Top border for header, bottom border for every row.
            if (isHeader)
            {
                EditorGUI.DrawRect(new Rect(row.x, row.y, row.width, 1f), BorderColor);
            }
            EditorGUI.DrawRect(new Rect(row.x, row.yMax - 1f, row.width, 1f), BorderColor);

            // Left and right outer borders.
            EditorGUI.DrawRect(new Rect(row.x, row.y, 1f, row.height), BorderColor);
            float totalWidth = 0f;
            for (int i = 0; i < widths.Length; i++)
                totalWidth += widths[i];
            EditorGUI.DrawRect(new Rect(row.x + totalWidth - 1f, row.y, 1f, row.height), BorderColor);

            // Column dividers between cells.
            float x = row.x;
            for (int i = 0; i < widths.Length - 1; i++)
            {
                x += widths[i];
                EditorGUI.DrawRect(new Rect(x, row.y, 1f, row.height), BorderColor);
            }
        }

        private static Rect CellRect(Rect row, float[] widths, int cellIndex)
        {
            float x = row.x;
            for (int i = 0; i < cellIndex; i++)
                x += widths[i];
            return new Rect(x + CellPadX, row.y, widths[cellIndex] - CellPadX * 2f, row.height);
        }

        // ─────────── Frame-data analysis ───────────

        private static List<BoxProps> CollectUniqueHitboxes(HitboxData data)
        {
            var result = new List<BoxProps>();
            if (data == null || data.Frames == null)
                return result;

            for (int f = 0; f < data.Frames.Count; f++)
            {
                FrameData frame = data.Frames[f];
                if (frame == null || frame.Boxes == null)
                    continue;
                for (int b = 0; b < frame.Boxes.Count; b++)
                {
                    BoxProps props = frame.Boxes[b].Props;
                    if (props.Kind == HitboxKind.Hurtbox)
                        continue;
                    if (!result.Contains(props))
                        result.Add(props);
                }
            }
            return result;
        }

        // Unique hitbox whose latest-occurring frame is the largest — the "last timing-wise" hit.
        private static int FindLatestUniqueBoxIndex(List<BoxProps> uniqueBoxes, HitboxData data)
        {
            if (uniqueBoxes.Count == 0 || data == null || data.Frames == null)
                return -1;

            for (int f = data.Frames.Count - 1; f >= 0; f--)
            {
                FrameData frame = data.Frames[f];
                if (frame == null || frame.Boxes == null)
                    continue;
                for (int b = 0; b < frame.Boxes.Count; b++)
                {
                    BoxProps props = frame.Boxes[b].Props;
                    if (props.Kind == HitboxKind.Hurtbox)
                        continue;
                    int idx = uniqueBoxes.IndexOf(props);
                    if (idx >= 0)
                        return idx;
                }
            }
            return -1;
        }

        // Start of the last contiguous run of frames that contain a hitbox with these exact props.
        private static int FindFirstFrameOfLastContiguousRun(HitboxData data, BoxProps refProps)
        {
            if (data == null || data.Frames == null)
                return -1;

            int lastRunStart = -1;
            bool inRun = false;
            for (int f = 0; f < data.Frames.Count; f++)
            {
                FrameData frame = data.Frames[f];
                bool has = false;
                if (frame != null && frame.Boxes != null)
                {
                    for (int b = 0; b < frame.Boxes.Count; b++)
                    {
                        BoxProps props = frame.Boxes[b].Props;
                        if (props.Kind == HitboxKind.Hurtbox)
                            continue;
                        if (props.Equals(refProps))
                        {
                            has = true;
                            break;
                        }
                    }
                }
                if (has && !inRun)
                {
                    lastRunStart = f;
                    inRun = true;
                }
                else if (!has)
                {
                    inRun = false;
                }
            }
            return lastRunStart;
        }

        private static string GatlingsFor(CharacterConfig config, CharacterState from)
        {
            if (config.Gatlings == null || config.Gatlings.Count == 0)
                return "";
            var sb = new StringBuilder();
            for (int i = 0; i < config.Gatlings.Count; i++)
            {
                GatlingEntry entry = config.Gatlings[i];
                if (entry.From != from)
                    continue;
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(entry.To.ToString());
            }
            return sb.ToString();
        }

        // ─────────── Label formatting ───────────

        private static string FormatOnHit(BoxProps props, int ticksFromReferenceFrame)
        {
            // Hard/soft knockdown overrides numeric advantage — the defender is on the ground,
            // so frame advantage isn't meaningful in the normal sense.
            switch (props.KnockdownKind)
            {
                case KnockdownKind.Heavy:
                    return "HKD";
                case KnockdownKind.Light:
                    return "SKD";
                default:
                    return FormatAdvantage(props.HitstunTicks - ticksFromReferenceFrame);
            }
        }

        private static string FormatAdvantage(int adv)
        {
            return adv > 0 ? "+" + adv : adv.ToString();
        }

        private static string AttackKindLabel(BoxProps props)
        {
            if (props.Kind == HitboxKind.Grabbox)
                return "—";
            switch (props.AttackKind)
            {
                case AttackKind.Medium:
                    return "Mid";
                case AttackKind.Overhead:
                    return "High";
                case AttackKind.Low:
                    return "Low";
                default:
                    return props.AttackKind.ToString();
            }
        }

        private static string KindLabel(HitboxKind kind)
        {
            switch (kind)
            {
                case HitboxKind.Hitbox:
                    return "Hit";
                case HitboxKind.Grabbox:
                    return "Grab";
                case HitboxKind.Hurtbox:
                    return "Hurt";
                default:
                    return kind.ToString();
            }
        }

        private static string KnockdownLabel(KnockdownKind kind)
        {
            switch (kind)
            {
                case KnockdownKind.Heavy:
                    return "Hard";
                case KnockdownKind.Light:
                    return "Soft";
                case KnockdownKind.None:
                    return "";
                default:
                    return kind.ToString();
            }
        }
    }
}
