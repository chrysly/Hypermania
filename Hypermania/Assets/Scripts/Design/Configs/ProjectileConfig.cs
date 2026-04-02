using System;
using Design.Animation;
using Game;
using Game.View.Projectiles;
using Utils.SoftFloat;

namespace Design.Configs
{
    [Serializable]
    public class ProjectileConfig
    {
        public CharacterState TriggerState;
        public int SpawnTick;
        public ProjectileView Prefab;
        public HitboxData HitboxData;
        public SVector2 SpawnOffset;
        public SVector2 Velocity;
        public int LifetimeTicks;
    }
}
