using MemoryPack;
using Utils;
using Utils.SoftFloat;

namespace Game.Sim
{
    [MemoryPackable]
    public partial struct ProjectileState
    {
        public bool Active;
        public int Owner;
        public SVector2 Position;
        public SVector2 Velocity;
        public Frame CreationFrame;
        public int LifetimeTicks;
        public FighterFacing FacingDir;
        public bool MarkedForDestroy;
        public int ConfigIndex;
    }
}
