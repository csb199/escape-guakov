using System;
using System.Collections.Generic;

namespace Game.Core
{
    public interface IEntityAttachmentProvider<T> where T : class
    {
        void Attach(T child, T parent, string socket, bool worldPositionStays);
        void Detach(T child);
    }
    public partial class EntityManager<T> where T : class
    {
        bool changingHierarchy;
        public void Attach(EntityHandle<T> child, EntityHandle<T> parent, string socket = null, bool worldPositionStays = true, object data = null)
        {
            RequireShown(child); RequireShown(parent);
            if (changingHierarchy) throw new InvalidOperationException("Attachment callbacks cannot change the hierarchy");
            if (child.Owner != parent.Owner) throw new InvalidOperationException("Attached entities must share an owner");
            for (var ancestor = parent; ancestor != null; ancestor = ancestor.Parent)
                if (ancestor == child) throw new InvalidOperationException("Entity attachment cycle");
            var adapter = provider as IEntityAttachmentProvider<T> ?? throw new NotSupportedException("Provider does not support attachment");
            changingHierarchy = true;
            try
            {
                DetachCore(child, data);
                // Detach callbacks may request teardown of either endpoint.
                RequireShown(child); RequireShown(parent);
                child.Parent = parent; parent.Children.Add(child);
                adapter.Attach(child.Instance, parent.Instance, socket, worldPositionStays);
                child.Logic.OnAttachTo(parent, data); parent.Logic.OnAttached(child, data);
                log.Write(Game.Contracts.GameLogLevel.Debug, "entity", "entity.attached", child.Id + " -> " + parent.Id);
            }
            catch (Exception error)
            {
                child.Broken = true;
                var errors = new List<Exception> { error };
                try { DetachCore(child, data); } catch (Exception cleanup) { errors.Add(cleanup); }
                RequestHide(child);
                throw new AggregateException("Entity attachment failed", errors);
            }
            finally { changingHierarchy = false; }
        }
        public void Detach(EntityHandle<T> child, object data = null)
        {
            RequireShown(child);
            if (changingHierarchy) throw new InvalidOperationException("Attachment callbacks cannot change the hierarchy");
            changingHierarchy = true;
            try { DetachCore(child, data); }
            catch { child.Broken = true; RequestHide(child); throw; }
            finally { changingHierarchy = false; }
        }
        void RequireShown(EntityHandle<T> entity)
        {
            if (!Contains(entity) || !entity.IsShown || entity.HideTask != null) throw new InvalidOperationException("Entity is not an active activation");
        }
        void EnsureCanHide(EntityHandle<T> entity)
        {
            for (var scope = StateScope.Executing; scope != null; scope = scope.Parent)
                if (scope.Owner is EntityHandle<T> executingEntity)
                    for (var ancestor = executingEntity; ancestor != null; ancestor = ancestor.Parent)
                        if (entity == ancestor) throw new InvalidOperationException("Entity tasks must request their own or ancestor teardown without awaiting it");
        }
        void DetachCore(EntityHandle<T> child, object data)
        {
            var parent = child.Parent; if (parent == null) return;
            child.Parent = null; parent.Children.Remove(child);
            var errors = new List<Exception>();
            bool previous = changingHierarchy; changingHierarchy = true;
            try
            {
                try { ((IEntityAttachmentProvider<T>)provider).Detach(child.Instance); } catch (Exception e) { errors.Add(e); }
                try { child.Logic.OnDetachFrom(parent, data); } catch (Exception e) { errors.Add(e); }
                try { parent.Logic.OnDetached(child, data); } catch (Exception e) { errors.Add(e); }
            }
            finally { changingHierarchy = previous; }
            if (errors.Count > 0) throw new AggregateException("Entity detach failed", errors);
        }
    }
}
