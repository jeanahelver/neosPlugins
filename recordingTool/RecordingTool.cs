using System;
using BaseX;
using FrooxEngine;
using CodeX;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using FrooxEngine.UIX;

namespace RecordingTool
{
    [Category("Tools/Tooltips")]

    public partial class RecordingTool : ToolTip
    {
        public readonly SyncRef<User> recordingUser;

        public readonly Sync<int> state;

        public readonly Sync<double> _startTime;

        public AnimX animation;

        public readonly SyncRef<Slot> rootSlot;
        public readonly Sync<bool> replaceRefs;

        public readonly SyncList<TrackedRig> recordedRigs;

        public readonly SyncList<TrackedSlot> recordedSlots;

        public readonly SyncRef<StaticAnimationProvider> _result;

        //public int animationTrackIndex = 0;

        //public readonly SyncList<ACMngr> trackedFields;

        protected override void OnAttach()
        {
            base.OnAttach();
            Slot visual = Slot.AddSlot("Visual");

            visual.LocalRotation = floatQ.Euler(90f, 0f, 0f);
            visual.LocalPosition = new float3(0, 0, 0);

            PBS_Metallic material = visual.AttachComponent<PBS_Metallic>();

            visual.AttachComponent<SphereCollider>().Radius.Value = 0.025f;

            ValueMultiplexer<color> vm = visual.AttachComponent<ValueMultiplexer<color>>();
            vm.Target.Target = material.EmissiveColor;
            vm.Values.Add(new color(0, 0.5f, 0, 1));
            vm.Values.Add(new color(0.5f, 0, 0, 1));
            vm.Values.Add(new color(0.5f, 0.5f, 0, 1));
            vm.Values.Add(new color(0, 0, 0.5f, 1));
            vm.Index.DriveFrom<int>(state);

            CylinderMesh mesh = visual.AttachMesh<CylinderMesh>(material);
            mesh.Radius.Value = 0.015f;
            mesh.Height.Value = 0.05f;
        }

        public override void OnPrimaryPress()
        {
            if (state.Value == 3)
            {
                Animator animator = rootSlot.Target.AttachComponent<Animator>();
                animator.Clip.Target = _result.Target;
                foreach (TrackedRig rig in recordedRigs) { rig.OnReplace(animator); }
                foreach (TrackedSlot slot in recordedSlots) { slot.OnReplace(animator); }
                //foreach (ACMngr field in trackedFields) { field.OnStop(); }
                state.Value = 0;
            }
            else if (state.Value == 1)
            {
                state.Value = 2;
                StartTask(bakeAsync);
            }
            else if (state.Value == 0)
            {
                animation = new AnimX(1f);
                recordingUser.Target = LocalUser;
                state.Value = 1;
                _startTime.Value = base.Time.WorldTime;
                foreach (TrackedRig rig in recordedRigs) { rig.OnStart(this); }
                foreach (TrackedSlot slot in recordedSlots) { slot.OnStart(this); }
                //foreach (ACMngr field in trackedFields) { field.OnStart(this); }
            }
        }

        protected override void OnCommonUpdate()
        {
            base.OnCommonUpdate();
            if (state.Value != 1) return;
            User usr = recordingUser.Target;
            if (usr == LocalUser)
            {
                float t = (float)(base.Time.WorldTime - _startTime);
                foreach (TrackedRig rig in recordedRigs) { rig.OnUpdate(t); }
                foreach (TrackedSlot slot in recordedSlots) { slot.OnUpdate(t); }
                //foreach (ACMngr field in trackedFields) { field.OnUpdate(t); }
            }
        }

        protected async Task bakeAsync()
        {
            float t = (float)(base.Time.WorldTime - _startTime);
            animation.GlobalDuration = t;

            foreach (TrackedRig rig in recordedRigs) { rig.OnStop(); }
            foreach (TrackedSlot slot in recordedSlots) { slot.OnStop(); }
            //foreach (ACMngr field in trackedFields) { field.OnStop(); }
            await default(ToBackground);

            string tempFilePath = Engine.LocalDB.GetTempFilePath("animx");
            animation.SaveToFile(tempFilePath);
            Uri uri = Engine.LocalDB.ImportLocalAsset(tempFilePath, LocalDB.ImportLocation.Move);

            await default(ToWorld);
            _result.Target = base.Slot.AttachComponent<StaticAnimationProvider>();
            _result.Target.URL.Value = uri;
            if (replaceRefs.Value)
                state.Value = 3;
            else
                state.Value = 0;
        }
    }
    public interface Trackable
    {
        void OnStart(RecordingTool rt);
        void OnUpdate(float T);
        void OnStop();
        void OnReplace(Animator anim);
        void Clean();
    }
    public class TrackedRig : SyncObject, Trackable
    {
        public readonly SyncRef<Rig> rig;
        public readonly Sync<bool> position;
        public readonly Sync<bool> rotation;
        public readonly Sync<bool> scale;
        public readonly SyncRef<RecordingTool> _rt;
        public readonly SyncRefList<Bonez> _trackedBones;
        //public Bonez[] bonezs;


        public void OnStart(RecordingTool rt)
        {
            if (rig.Target == null) return;
            _rt.Target = rt;
            bool pos = position.Value;
            bool rot = rotation.Value;
            bool scl = scale.Value;
            //bonezs = new Bonez[rig.Target.Bones.Count];
            foreach (Slot bone in rig.Target.Bones)
            {
                Bonez b = new Bonez();
                World.ReferenceController.LocalAllocationBlockBegin();
                b.Initialize(World, _trackedBones);
                World.ReferenceController.LocalAllocationBlockEnd();
                _trackedBones.Add(b);
                b.OnStart(this, bone, pos, rot, scl);
            }
        }
        public void OnUpdate(float T)
        {
            foreach (Bonez b in _trackedBones)
            {
                b?.OnUpdate(T);
            }
        }
        public void OnStop() { }
        public class Bonez : SyncObject, ICustomInspector
        {
            public readonly SyncRef<Slot> slot;
            public TrackedRig rig;
            public CurveFloat3AnimationTrack positionTrack;
            public CurveFloatQAnimationTrack rotationTrack;
            public CurveFloat3AnimationTrack scaleTrack;

            public void OnStart(TrackedRig r, Slot sloot, bool position, bool rotation, bool scale)
            {
                rig = r;
                slot.Target = sloot;
                if (position) positionTrack = r._rt.Target.animation.AddTrack<CurveFloat3AnimationTrack>();
                if (rotation) rotationTrack = r._rt.Target.animation.AddTrack<CurveFloatQAnimationTrack>();
                if (scale) scaleTrack = r._rt.Target.animation.AddTrack<CurveFloat3AnimationTrack>();
            }
            public void OnUpdate(float T)
            {
                Slot ruut = rig._rt.Target.rootSlot.Target;
                positionTrack?.InsertKeyFrame(ruut.GlobalPointToLocal(slot.Target?.GlobalPosition ?? float3.Zero), T);
                rotationTrack?.InsertKeyFrame(ruut.GlobalRotationToLocal(slot.Target?.GlobalRotation ?? floatQ.Identity), T);
                scaleTrack?.InsertKeyFrame(ruut.GlobalVectorToLocal(slot.Target?.GlobalScale ?? float3.Zero), T);
            }
            public void OnStop()
            {

            }
            public void BuildInspectorUI(UIBuilder ui)
            {
                ui.PushStyle();
                ui.Style.MinHeight = 24f;
                ui.Panel();
                ui.Text("<Tracked slot>");
                ui.NestOut();
                ui.PopStyle();
            }
            public void OnReplace(Animator anim)
            {
                Slot root = rig._rt.Target.rootSlot.Target;
                Slot s = root.AddSlot(slot.Name);
                if (positionTrack != null) { anim.Fields.Add().Target = s.Position_Field; }
                if (rotationTrack != null) { anim.Fields.Add().Target = s.Rotation_Field; }
                if (scaleTrack != null) { anim.Fields.Add().Target = s.Scale_Field; }
                //World.ReplaceReferenceTargets(slot, s, true);
                World.ForeachWorldElement(delegate (ISyncRef syncRef){
                    if (syncRef.Target == slot)
                        syncRef.Target = s;
                }, root);
            }
            public void Clean()
            {
                positionTrack = null; rotationTrack = null; scaleTrack = null;
            }
        }
        public void OnReplace(Animator anim)
        {
            foreach(Bonez b in _trackedBones)
            {
                b?.OnReplace(anim);
            }
        }
        public void Clean()
        {
            foreach(Bonez b in _trackedBones) { b.Clean(); }
        }
    }
    public class TrackedSlot : SyncObject, Trackable
    {
        public readonly SyncRef<Slot> slot;
        public readonly Sync<bool> position;
        public readonly Sync<bool> rotation;
        public readonly Sync<bool> scale;
        public readonly SyncRef<RecordingTool> _rt;

        public CurveFloat3AnimationTrack positionTrack;
        public CurveFloatQAnimationTrack rotationTrack;
        public CurveFloat3AnimationTrack scaleTrack;

        public void OnStart(RecordingTool rt)
        {
            _rt.Target = rt;
            if (position.Value) positionTrack = rt.animation.AddTrack<CurveFloat3AnimationTrack>();
            if (rotation.Value) rotationTrack = rt.animation.AddTrack<CurveFloatQAnimationTrack>();
            if (scale.Value) scaleTrack = rt.animation.AddTrack<CurveFloat3AnimationTrack>();
        }
        public void OnUpdate(float T)
        {
            Slot ruut = _rt.Target.rootSlot.Target;
            positionTrack?.InsertKeyFrame(ruut.GlobalPointToLocal(slot.Target?.GlobalPosition ?? float3.Zero), T);
            rotationTrack?.InsertKeyFrame(ruut.GlobalRotationToLocal(slot.Target?.GlobalRotation ?? floatQ.Identity), T);
            scaleTrack?.InsertKeyFrame(ruut.GlobalVectorToLocal(slot.Target?.GlobalScale ?? float3.Zero), T);
        }
        public void OnStop(){ }
        public void OnReplace(Animator anim)
        {
            Slot root = _rt.Target.rootSlot.Target;
            Slot s = root.AddSlot(slot.Name);
            if (positionTrack != null) { anim.Fields.Add().Target = s.Position_Field; }
            if (rotationTrack != null) { anim.Fields.Add().Target = s.Rotation_Field; }
            if (scaleTrack != null) { anim.Fields.Add().Target = s.Scale_Field; }
            //World.ReplaceReferenceTargets(slot, s, true);
            World.ForeachWorldElement(delegate (ISyncRef syncRef) {
                if (syncRef.Target == slot)
                    syncRef.Target = s;
            }, root);
        }
        public void Clean()
        {
            positionTrack = null; rotationTrack = null; scaleTrack = null;
        }
    }
}
