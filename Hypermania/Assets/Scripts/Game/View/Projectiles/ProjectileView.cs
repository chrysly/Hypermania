using Game.Sim;
using UnityEngine;
using Utils;

namespace Game.View.Projectiles
{
    [RequireComponent(typeof(Animator))]
    public abstract class ProjectileView : EntityView
    {
        protected Animator _animator;

        public virtual void Awake()
        {
            _animator = GetComponent<Animator>();
            _animator.speed = 0f;
        }

        public virtual void Init() { }

        public abstract void Render(Frame simFrame, in ProjectileState state);

        public virtual void DeInit() { }
    }
}
