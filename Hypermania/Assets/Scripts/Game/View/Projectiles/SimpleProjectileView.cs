using Design.Animation;
using Design.Configs;
using Game.Sim;
using UnityEngine;
using Utils;

namespace Game.View.Projectiles
{
    public class SimpleProjectileView : ProjectileView
    {
        [SerializeField]
        private ProjectileConfig _config;

        public override void Render(Frame simFrame, in ProjectileState state)
        {
            Vector3 pos = transform.position;
            pos.x = (float)state.Position.x;
            pos.y = (float)state.Position.y;
            transform.position = pos;

            transform.localScale = new Vector3(state.FacingDir == FighterFacing.Left ? -1 : 1, 1f, 1f);

            HitboxData data = state.IsDying ? _config.OnDeathHitbox : _config.HitboxData;
            int tick = state.IsDying ? simFrame - state.DeathFrame : simFrame - state.CreationFrame;

            if (data == null || data.Clip == null)
                return;

            float normalizedTime = data.GetAnimNormalizedTime(tick);
            _animator.Play(data.Clip.name, 0, normalizedTime);
            _animator.Update(0f);
        }
    }
}
