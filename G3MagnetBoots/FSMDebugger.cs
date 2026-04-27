using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace G3MagnetBoots
{
    /// <summary>
    /// Attach to a KerbalEVA GameObject to log all FSM state transitions and event fires.
    /// Entirely gated on Logger.IsDebugMode — zero overhead when debug is off.
    /// </summary>
    internal sealed class FSMDebugger : MonoBehaviour
    {
        // ---- static factory ----

        /// <summary>Attach or refresh the debugger on the given KerbalEVA.</summary>
        internal static void Attach(KerbalEVA eva)
        {
            if (eva == null) return;
            var existing = eva.gameObject.GetComponent<FSMDebugger>();
            if (existing != null)
            {
                existing._eva = eva;
                existing.RehookEvents();
                return;
            }
            var d = eva.gameObject.AddComponent<FSMDebugger>();
            d._eva = eva;
        }

        // ---- instance ----

        private KerbalEVA _eva;
        private string _lastStateName = "";

        // event name -> hooked delegate (so we can un-hook cleanly on destroy/rehook)
        private readonly Dictionary<string, Delegate> _hookedDelegates = new();

        private void Start()
        {
            if (_eva == null) { Destroy(this); return; }
            RehookEvents();
        }

        private void OnDestroy()
        {
            UnhookEvents();
        }

        // ---- public re-entry point (called when FSM is rebuilt) ----

        internal void RehookEvents()
        {
            UnhookEvents();
            HookAllEvents();
            _lastStateName = _eva?.fsm?.CurrentState?.name ?? "<null>";
        }

        // ---- state polling ----

        private void LateUpdate()
        {
            if (!Logger.IsDebugMode) return;
            if (_eva?.fsm == null) return;

            string current = _eva.fsm.CurrentState?.name ?? "<null>";
            if (current != _lastStateName)
            {
                Logger.Debug($"[FSM:{_eva.name}] State  {_lastStateName}  ->  {current}");
                _lastStateName = current;
            }
        }

        // ---- event reflection ----

        // KFSMEvent.OnEvent is a KFSMEventCallback (delegate void KFSMEventCallback())
        // We get it via reflection so we don't need a hard reference to KFSMEventCallback type.
        private static readonly FieldInfo _onEventField =
            typeof(KFSMEvent).GetField("OnEvent", BindingFlags.Public | BindingFlags.Instance)
            ?? typeof(KFSMEvent).GetField("onEvent", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo _fsmEventsField =
            typeof(KerbalFSM).GetField("Events", BindingFlags.Public | BindingFlags.Instance)
            ?? typeof(KerbalFSM).GetField("events", BindingFlags.NonPublic | BindingFlags.Instance);

        private void HookAllEvents()
        {
            if (_eva?.fsm == null || _onEventField == null) return;

            // Collect all events registered in the FSM via its internal list/dict
            var events = CollectAllEvents(_eva.fsm);

            foreach (var evt in events)
            {
                string evtName = evt.name;
                if (_hookedDelegates.ContainsKey(evtName)) continue; // already hooked

                Delegate handler = MakeHandler(evtName);
                _hookedDelegates[evtName] = handler;

                // Append our handler to the existing OnEvent multicast delegate
                var existing = _onEventField.GetValue(evt) as Delegate;
                var combined = Delegate.Combine(existing, handler);
                _onEventField.SetValue(evt, combined);
            }
        }

        private void UnhookEvents()
        {
            if (_eva?.fsm == null || _onEventField == null || _hookedDelegates.Count == 0) return;

            var events = CollectAllEvents(_eva.fsm);
            foreach (var evt in events)
            {
                if (!_hookedDelegates.TryGetValue(evt.name, out var handler)) continue;
                var existing = _onEventField.GetValue(evt) as Delegate;
                var trimmed = Delegate.Remove(existing, handler);
                _onEventField.SetValue(evt, trimmed);
            }
            _hookedDelegates.Clear();
        }

        private Delegate MakeHandler(string evtName)
        {
            string name = evtName;
            string kerbalName = _eva?.name ?? "?";

            Action action = () =>
            {
                if (!Logger.IsDebugMode) return;
                string state = _eva?.fsm?.CurrentState?.name ?? "<null>";
                Logger.Debug($"[FSM:{kerbalName}] Event fired: {name}  (in state: {state})");
            };

            return Delegate.CreateDelegate(_onEventField.FieldType, action.Target, action.Method);
        }

        // KerbalFSM stores states in a private/protected list — reflect to find it.
        private static readonly FieldInfo _fsmStateListField = FindListField(typeof(KerbalFSM), typeof(KFSMState));

        private static FieldInfo FindListField(Type owner, Type elementType)
        {
            foreach (var f in owner.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var ft = f.FieldType;
                if (ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(List<>)
                    && ft.GetGenericArguments()[0] == elementType)
                    return f;
            }
            return null;
        }

        // KFSMState stores its registered events in a list — reflect to find it.
        private static readonly FieldInfo _stateEventListField = FindListField(typeof(KFSMState), typeof(KFSMEvent));

        private static List<KFSMEvent> CollectAllEvents(KerbalFSM fsm)
        {
            var result = new HashSet<KFSMEvent>(ReferenceEqualityComparer.Instance);

            // Walk all states via reflection
            if (_fsmStateListField != null)
            {
                var stateList = _fsmStateListField.GetValue(fsm) as System.Collections.IEnumerable;
                if (stateList != null)
                {
                    foreach (var stateObj in stateList)
                    {
                        if (stateObj is not KFSMState state) continue;
                        if (_stateEventListField == null) continue;
                        var evtList = _stateEventListField.GetValue(state) as System.Collections.IEnumerable;
                        if (evtList == null) continue;
                        foreach (var evtObj in evtList)
                            if (evtObj is KFSMEvent evt) result.Add(evt);
                    }
                }
            }

            // Also try a top-level Events field if it exists
            if (_fsmEventsField != null)
            {
                var topLevel = _fsmEventsField.GetValue(fsm);
                if (topLevel is IEnumerable<KFSMEvent> topEvts)
                    foreach (var e in topEvts)
                        if (e != null) result.Add(e);
            }

            return new List<KFSMEvent>(result);
        }

        // Simple reference-equality comparer for the HashSet
        private sealed class ReferenceEqualityComparer : IEqualityComparer<KFSMEvent>
        {
            internal static readonly ReferenceEqualityComparer Instance = new();
            public bool Equals(KFSMEvent x, KFSMEvent y) => ReferenceEquals(x, y);
            public int GetHashCode(KFSMEvent obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
