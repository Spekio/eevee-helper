﻿using Celeste.Mod.EeveeHelper.Components;
using Celeste.Mod.Entities;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.EeveeHelper.Entities {
    [Tracked]
    [CustomEntity("EeveeHelper/HoldableContainer")]
    public class HoldableContainer : Actor {
        public bool HasGravity {
            get => hasGravity && Container?.Contained.Count > 0;
            set => hasGravity = value;
        }

        private bool hasGravity;
        private bool holdable;
        private bool noDuplicate;
        private bool destroyable;

        public EntityID ID;
        public EntityContainerMover Container;
        public Vector2 Speed;
        public Holdable Hold;
        private Dictionary<Entity, bool> wasCollidable = new Dictionary<Entity, bool>();
        private float noGravityTimer;
        private float highFrictionTimer;
        private bool destroyed;
        private bool slowFall;
        private float whiteAlpha;
        private Vector2 prevLiftSpeed;
        private Vector2 holdTarget = Vector2.Zero;

        public HoldableContainer(EntityData data, Vector2 offset, EntityID id) : base(data.Position + offset + new Vector2(data.Width / 2f, data.Height)) {
            holdable = data.Bool("holdable");
            hasGravity = data.Bool("gravity");
            noDuplicate = data.Bool("noDuplicate");
            destroyable = data.Bool("destroyable", true);
            slowFall = data.Bool("slowFall");

            ID = id;
            Collider = new Hitbox(data.Width, data.Height);
            Collider.Position = new Vector2(-Width / 2f, -Height);
            AllowPushing = HasGravity;
            Depth = Depths.Top - 10;

            Add(Container = new EntityContainerMover(data) {
                DefaultIgnored = e => e is HoldableContainer,
                OnFit = OnFit,
                OnPreMove = () => AllowPushing = false,
                OnPostMove = () => AllowPushing = HasGravity,
            });

            if (holdable) {
                Add(Hold = new Holdable() {
                    PickupCollider = new Hitbox(Width + 8f, Height + 8f, -Width / 2f - 4f, -Height - 4f),
                    SlowFall = slowFall,
                    SlowRun = data.Bool("slowRun", true),
                    OnPickup = OnPickup,
                    OnRelease = OnRelease,
                    OnHitSpring = HitSpring,
                    SpeedGetter = () => Speed,
                    OnCarry = OnCarry
                });

                Add(new DepthPartner(Depths.Player + 1) {
                    OnUpdate = () => {
                        if (Hold.IsHeld || destroyed) {
                            foreach (var entity in Container.Contained)
                                entity.Collidable = false;
                        }
                    }
                });
            }

            SquishCallback = OnSquish;
        }

        public override void Awake(Scene scene) {
            base.Awake(scene);
            foreach (HoldableContainer container in scene.Tracker.GetEntities<HoldableContainer>()) {
                if (noDuplicate && container != this && container.ID.Key == ID.Key && container.Hold != null && container.Hold.IsHeld) {
                    RemoveSelf();
                }
            }
        }

        private void OnPickup() {
            Speed = Vector2.Zero;
            AddTag(Tags.Persistent);
            wasCollidable.Clear();
            foreach (var entity in Container.Contained) {
                wasCollidable.Add(entity, entity.Collidable);
                entity.AddTag(Tags.Persistent);
                entity.Collidable = false;
            }
            highFrictionTimer = 0.5f;
        }

        private void OnRelease(Vector2 force) {
            if (!destroyed && CollideCheck<Solid>()) {
                ReleasedSquishWiggle();
                force = Vector2.Zero;
            }
            RemoveTag(Tags.Persistent);
            if (!destroyed) {
                foreach (var entity in Container.Contained) {
                    entity.RemoveTag(Tags.Persistent);
                    entity.Collidable = wasCollidable.ContainsKey(entity) ? wasCollidable[entity] : true;
                }
            }
            if (slowFall)
                force.Y *= 0.5f;
            if (force.X != 0f && force.Y == 0f)
                force.Y = -0.4f;
            Speed = force * (slowFall ? 100f : 200f);
            if (Speed != Vector2.Zero)
                noGravityTimer = 0.1f;
        }

        private void OnCarry(Vector2 target) {
            holdTarget = target;
            Container.DoMoveAction(() => Position = target);
        }

        protected override void OnSquish(CollisionData data) {
            bool wiggled = false;
            Container.DoMoveAction(() => wiggled = TryBigSquishWiggle(data));
            return;
        }

        private bool TryBigSquishWiggle(CollisionData data) {
            data.Pusher.Collidable = true;
            for (int i = 0; i <= Math.Max(3, (int)Width/2); i++) {
                for (int j = 0; j <= Math.Max(3, (int)Height/2); j++) {
                    if (i != 0 || j != 0) {
                        for (int k = 1; k >= -1; k -= 2) {
                            for (int l = 1; l >= -1; l -= 2) {
                                var value = new Vector2(i * k, j * l);
                                if (!CollideCheck<Solid>(Position + value)) {
                                    Position += value;
                                    data.Pusher.Collidable = false;
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            data.Pusher.Collidable = false;
            return false;
        }

        private void ReleasedSquishWiggle() {
            Container.DoMoveAction(() => {
                for (int i = 0; i <= Math.Max(3, (int)Width); i++) {
                    for (int j = 0; j <= Math.Max(3, (int)Height); j++) {
                        if (i != 0 || j != 0) {
                            for (int k = 1; k >= -1; k -= 2) {
                                for (int l = 1; l >= -1; l -= 2) {
                                    var value = new Vector2(i * k, j * l);
                                    if (!CollideCheck<Solid>(Position + value)) {
                                        Position += value;
                                        return;
                                    }
                                }
                            }
                        }
                    }
                }
            });
        }

        private void OnFit(Vector2 pos, float width, float height) {
            Position = new Vector2(pos.X + width / 2f, pos.Y + height);
            Collider.Position = new Vector2(-width / 2f, -height);
            Collider.Width = width;
            Collider.Height = height;
            if (holdable) {
                Hold.PickupCollider.Position = Collider.Position - new Vector2(4f, 4f);
                Hold.PickupCollider.Width = Collider.Width + 8f;
                Hold.PickupCollider.Height = Collider.Height + 8f;

                if (Hold.IsHeld)
                    Container.DoMoveAction(() => Position = holdTarget);
            }
        }

        public override void Update() {
            base.Update();

            AllowPushing = HasGravity;
            Collidable = !destroyed && Container.Contained.Count > 0;

            if (!Collidable && holdable && Hold.IsHeld) {
                var speed = Hold.Holder.Speed;
                Hold.Holder.Drop();
                Speed = speed * 0.5f;
                Input.Rumble(RumbleStrength.Medium, RumbleLength.Medium);
            }

            if (!destroyed) {
                if (Container.Contained.Count == 0) return;

                if (destroyable) {
                    foreach (SeekerBarrier barrier in Scene.Tracker.GetEntities<SeekerBarrier>()) {
                        barrier.Collidable = true;
                        var collided = CollideCheck(barrier) && !Container.Contained.Contains(barrier);
                        barrier.Collidable = false;

                        if (collided) {
                            destroyed = true;
                            Collidable = false;
                            if (holdable && Hold.IsHeld) {
                                var speed = Hold.Holder.Speed;
                                Hold.Holder.Drop();
                                Speed = speed * 0.5f;
                                Input.Rumble(RumbleStrength.Medium, RumbleLength.Medium);
                            }
                            Add(new Coroutine(DestroyRoutine()));
                            return;
                        }
                    }
                }
            } else {
                foreach (var entity in Container.Contained)
                    entity.Collidable = false;
            }

            if (holdable && Hold.IsHeld) {
                prevLiftSpeed = Vector2.Zero;
                foreach (var entity in Container.Contained) {
                    entity.Collidable = false;
                }
            } else {
                holdTarget = Vector2.Zero;
                if (highFrictionTimer > 0f)
                    highFrictionTimer -= Engine.DeltaTime;
                var level = SceneAs<Level>();
                Spring spring = null;
                if (!holdable) {
                    foreach (var entity in Scene.Entities)
                        if (entity is Spring && CollideCheck(entity))
                            spring = (Spring)entity;
                }
                if (spring != null) {
                    HitSpring(spring);
                    EeveeUtils.m_SpringBounceAnimate.Invoke(spring, new object[] { });
                } else if(OnGround(1)) {
                    float target;
                    if (!OnGround(Position + Vector2.UnitX * 3f, 1)) {
                        target = 20f;
                    } else if (!OnGround(Position - Vector2.UnitX * 3f, 1)) {
                        target = -20f;
                    } else {
                        target = 0f;
                    }
                    Speed.X = Calc.Approach(Speed.X, target, 800f * Engine.DeltaTime);
                    var liftSpeed = LiftSpeed;
                    if (liftSpeed == Vector2.Zero && prevLiftSpeed != Vector2.Zero) {
                        Speed = prevLiftSpeed;
                        prevLiftSpeed = Vector2.Zero;
                        Speed.Y = Math.Min(Speed.Y * 0.6f, 0f);
                        if (Speed.X != 0f && Speed.Y == 0f) {
                            Speed.Y = -60f;
                        }
                        if (Speed.Y < 0f) {
                            noGravityTimer = 0.15f;
                        }
                    } else {
                        prevLiftSpeed = liftSpeed;
                        if (liftSpeed.Y < 0f && Speed.Y < 0f) {
                            Speed.Y = 0f;
                        }
                    }
                } else if (!holdable || Hold.ShouldHaveGravity) {
                    float xAccel;
                    float yAccel;
                    if (!slowFall) {
                        yAccel = 800f;
                        if (Math.Abs(Speed.Y) <= 30f) {
                            yAccel *= 0.5f;
                        }
                        xAccel = 350f;
                        if (Speed.Y < 0f) {
                            xAccel *= 0.5f;
                        }
                    } else {
                        yAccel = 200f;
                        if (Speed.Y >= -30f) {
                            yAccel *= 0.5f;
                        }
                        if (Speed.Y < 0f) {
                            xAccel = 40f;
                        } else if (highFrictionTimer <= 0f) {
                            xAccel = 40f;
                        } else {
                            xAccel = 10f;
                        }
                    }
                    Speed.X = Calc.Approach(Speed.X, 0f, xAccel * Engine.DeltaTime);
                    if (noGravityTimer > 0f) {
                        noGravityTimer -= Engine.DeltaTime;
                    } else {
                        Speed.Y = Calc.Approach(Speed.Y, slowFall ? 30f : 200f, yAccel * Engine.DeltaTime);
                    }
                }
                if (!HasGravity)
                    Speed.Y = 0;
                MoveH(Speed.X * Engine.DeltaTime, OnCollideH, null);
                MoveV(Speed.Y * Engine.DeltaTime, OnCollideV, null);
                if (Left < level.Bounds.Left) {
                    Container.DoMoveAction(() => Left = level.Bounds.Left);
                    Speed.X = Speed.X * -0.4f;
                } else if (Top < (level.Bounds.Top - 4)) {
                    Container.DoMoveAction(() => Top = level.Bounds.Top + 4);
                    Speed.Y = 0f;
                } else if (Top > level.Bounds.Bottom) {
                    RemoveSelf();
                    return;
                }
            }
            Hold?.CheckAgainstColliders();
        }

        public override void Render() {
            base.Render();
            Draw.Rect(Collider, Color.White * 0.8f * whiteAlpha);
        }

        public override bool IsRiding(Solid solid) {
            return HasGravity && !Container.Contained.Contains(solid) && base.IsRiding(solid);
        }

        public override bool IsRiding(JumpThru jumpThru) {
            return HasGravity && !Container.Contained.Contains(jumpThru) && base.IsRiding(jumpThru);
        }

        private IEnumerator DestroyRoutine() {
            Audio.Play(SFX.game_10_glider_emancipate, Position);
            var tween = Tween.Create(Tween.TweenMode.Oneshot, null, 0.2f, true);
            tween.OnUpdate = (t) => whiteAlpha = t.Eased;
            Add(tween);
            yield return 0.2f;
            Container.FitContained = false;
            Container.RemoveContained();
            var tween2 = Tween.Create(Tween.TweenMode.Oneshot, null, 0.1f, true);
            tween2.OnUpdate = (t) => whiteAlpha = (1f - t.Eased);
            Add(tween2);
            yield return 0.1f;
            RemoveSelf();
        }

        private void OnCollideH(CollisionData data) {
            if (data.Hit is DashSwitch) {
                (data.Hit as DashSwitch).OnDashCollide(null, Vector2.UnitX * Math.Sign(Speed.X));
            }
            Speed.X = Speed.X * -0.4f;
        }

        private void OnCollideV(CollisionData data) {
            if (data.Hit is DashSwitch) {
                (data.Hit as DashSwitch).OnDashCollide(null, Vector2.UnitY * Math.Sign(Speed.Y));
            }
            Speed.Y = 0f;
        }

        public bool HitSpring(Spring spring) {
            if (!holdable || !Hold.IsHeld) {
                if (spring.Orientation == Spring.Orientations.Floor && Speed.Y >= 0f) {
                    Speed.X = Speed.X * 0.5f;
                    Speed.Y = -160f;
                    noGravityTimer = 0.15f;
                    return true;
                }
                if (spring.Orientation == Spring.Orientations.WallLeft && Speed.X <= 0f) {
                    MoveTowardsY(spring.CenterY + 5f, 4f, null);
                    Speed.X = slowFall ? 160f : 220f;
                    Speed.Y = -80f;
                    noGravityTimer = 0.1f;
                    return true;
                }
                if (spring.Orientation == Spring.Orientations.WallRight && Speed.X >= 0f) {
                    MoveTowardsY(spring.CenterY + 5f, 4f, null);
                    Speed.X = slowFall ? -160f : -220f;
                    Speed.Y = -80f;
                    noGravityTimer = 0.1f;
                    return true;
                }
            }
            return false;
        }
    }
}