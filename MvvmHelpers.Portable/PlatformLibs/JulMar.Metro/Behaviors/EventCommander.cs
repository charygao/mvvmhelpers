﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Input;

using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace JulMar.Behaviors
{
    /// <summary>
    /// This class manages a collection of command to event mappings.  It is used to wire up View events to a
    /// ViewModel ICommand implementation. 
    /// </summary>
    /// <example>
    /// <![CDATA[
    /// 
    /// <Behaviors:EventCommander.Mappings>
    ///    <Behaviors:CommandEvent Command="{Binding PointerEnterCommand}" Event="PointerEnter" />
    ///    <Behaviors:CommandEvent Command="{Binding PointerLeaveCommand}" Event="PointerLeave" />
    /// </Behaviors:EventCommander.Mappings>
    /// 
    /// ]]>
    /// </example>
    public static class EventCommander
    {
        /// <summary>
        /// Mappings backing store
        /// </summary>
        public static readonly DependencyProperty MappingsProperty = DependencyProperty.RegisterAttached("Mappings",
                            typeof(CommandEventCollection), typeof(EventCommander), 
                            new PropertyMetadata(null, OnMappingsChanged));

        /// <summary>
        /// Retrieves the mapping collection
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static CommandEventCollection GetMappings(DependencyObject obj)
        {
            var map = obj.GetValue(MappingsProperty) as CommandEventCollection;
            if (map == null)
            {
                map = new CommandEventCollection();
                SetMappings(obj, map);
            }
            return map;
        }

        /// <summary>
        /// This sets the mapping collection.
        /// </summary>
        /// <param name="obj">Dependency Object</param>
        /// <param name="value">Mapping collection</param>
        public static void SetMappings(DependencyObject obj, CommandEventCollection value)
        {
            obj.SetValue(MappingsProperty, value);
        }

        /// <summary>
        /// This changes the event mapping
        /// </summary>
        /// <param name="target"></param>
        /// <param name="e"></param>
        private static void OnMappingsChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            var sender = target as FrameworkElement;
            if (sender != null)
            {
                if (e.OldValue != null)
                {
                    CommandEventCollection cec = e.OldValue as CommandEventCollection;
                    if (cec != null)
                        cec.Unsubscribe(target);
                }
                if (e.NewValue != null)
                {
                    CommandEventCollection cec = e.NewValue as CommandEventCollection;
                    if (cec != null)
                        cec.Subscribe(target);
                }
            }
        }
    }

    /// <summary>
    /// This is passed to the ICommand handler for the event
    /// </summary>
    public class EventParameters
    {
        /// <summary>
        /// The sender of the handled event
        /// </summary>
        public object Sender { get; set; }

        /// <summary>
        /// The passed EventArgs for the event.
        /// </summary>
        public object EventArgs { get; set; }

        /// <summary>
        /// The ICommand which has just been executed
        /// </summary>
        public ICommand Command { get; set; }

        /// <summary>
        /// The associated CommandParameter (if any).
        /// </summary>
        public object CommandParameter { get; set; }

        /// <summary>
        /// Constructor for the EventParameters
        /// </summary>
        /// <param name="command">ICommand</param>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Event args</param>
        /// <param name="parameter">CommandParameter</param>
        internal EventParameters(ICommand command, object sender, object e, object parameter)
        {
            this.Command = command;
            this.Sender = sender;
            this.EventArgs = e;
            this.CommandParameter = parameter;
        }
    }

    /// <summary>
    /// This represents a single event to command mapping. 
    /// </summary>
    public class CommandEvent : FrameworkElement
    {
        /// <summary>
        /// Command Property Dependency Property
        /// </summary>
        public static readonly DependencyProperty CommandProperty =
            DependencyProperty.Register("Command", typeof(ICommand), typeof(CommandEvent), new PropertyMetadata(null));

        /// <summary>
        /// Parameter for the ICommand
        /// </summary>
        public static readonly DependencyProperty CommandParameterProperty =
            DependencyProperty.Register("CommandParameter", typeof(object), typeof(CommandEvent), new PropertyMetadata(null));

        /// <summary>
        /// Event Dependency Property
        /// </summary>
        public static readonly DependencyProperty EventProperty =
            DependencyProperty.Register("Event", typeof(string), typeof(CommandEvent), new PropertyMetadata(string.Empty));

        /// <summary>
        /// Gets or sets the Event property.
        /// </summary>
        public string Event
        {
            get { return (string)this.GetValue(EventProperty); }
            set { this.SetValue(EventProperty, value); }
        }

        /// <summary>
        /// Gets or sets the Command property. 
        /// </summary>
        public ICommand Command
        {
            get { return (ICommand)this.GetValue(CommandProperty); }
            set { this.SetValue(CommandProperty, value); }
        }

        /// <summary>
        /// Gets or sets the CommandParameter property.
        /// </summary>
        public object CommandParameter
        {
            get { return this.GetValue(CommandParameterProperty); }
            set { this.SetValue(CommandParameterProperty, value); }
        }

        /// <summary>
        /// Wires up an event to the target
        /// </summary>
        /// <param name="target"></param>
        internal void Subscribe(object target)
        {
            string eventName = this.Event;

            if (target != null)
            {
                this.SetBinding(DataContextProperty, new Binding { Source = target, Path = new PropertyPath("DataContext"), Mode = BindingMode.OneWay });

                EventInfo ei = this.LookForEventDeclaration(target, eventName);
                if (ei != null)
                {
                    var handler = this.GetEventMethod(ei);

                    // WinRT events cannot be subscribed to through normal events because it uses
                    // event tokens to map events to handlers. The compiler emits calls through WRM to do the work.
                    if (ei.DeclaringType.GetTypeInfo().Attributes.HasFlag(TypeAttributes.WindowsRuntime))
                    {
                        WindowsRuntimeMarshal.AddEventHandler(
                            d => (EventRegistrationToken) ei.AddMethod.Invoke(target, new object[] {d}),
                            et => ei.RemoveMethod.Invoke(target, new object[] {et}), handler);
                    }
                    else
                    {
                        ei.AddEventHandler(target, handler);
                    }
                }
            }

            Debug.WriteLine(string.Format("Unable to locate event {0} on {1}", eventName, target));
        }

        /// <summary>
        /// This searches the type for a given event - including ancestor classes.
        /// </summary>
        /// <param name="target"></param>
        /// <param name="eventName"></param>
        /// <returns></returns>
        private EventInfo LookForEventDeclaration(object target, string eventName)
        {
            Type currentType = target.GetType();
            while (currentType != typeof(object))
            {
                TypeInfo typeInfo = currentType.GetTypeInfo();
                EventInfo eventInfo = typeInfo.GetDeclaredEvent(eventName);
                if (eventInfo != null)
                    return eventInfo;

                currentType = typeInfo.BaseType;
            }

            return null;
        }

        /// <summary>
        /// Unwires target event
        /// </summary>
        /// <param name="target"></param>
        internal void Unsubscribe(object target)
        {
            if (target != null)
            {
                EventInfo ei = this.LookForEventDeclaration(target, this.Event);
                if (ei != null)
                {
                    var handler = this.GetEventMethod(ei);

                    // WinRT events cannot be subscribed to through normal events because it uses
                    // event tokens to map events to handlers. The compiler emits calls through WRM to do the work.
                    if (ei.DeclaringType.GetTypeInfo().Attributes.HasFlag(TypeAttributes.WindowsRuntime))
                    {
                        WindowsRuntimeMarshal.RemoveEventHandler(
                            et => ei.RemoveMethod.Invoke(target, new object[] {et}), handler);
                    }
                    else
                    {
                        ei.RemoveEventHandler(target, handler);
                    }
                }
            }
        }

        private Delegate _method;

        /// <summary>
        /// Retrieves a delegate to our OnEventRaised method which may be used with a specific
        /// EventHandler subtype based on an EventInfo.
        /// </summary>
        /// <param name="ei">Event to generate</param>
        /// <returns></returns>
        private Delegate GetEventMethod(EventInfo ei)
        {
            if (ei == null)
                throw new ArgumentNullException("ei");
            if (ei.EventHandlerType == null)
                throw new ArgumentException("EventHandlerType is null");
            if (this._method == null)
            {
                MethodInfo methodInfo = this.GetType().GetTypeInfo().DeclaredMethods.FirstOrDefault(
                        mi => mi.Name == "OnEventRaised" && mi.IsPrivate && !mi.IsStatic);

                this._method = methodInfo.CreateDelegate(ei.EventHandlerType, this);
            }

            return this._method;
        }

        /// <summary>
        /// This is invoked by the event - it invokes the command.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnEventRaised(object sender, object e)
        {
            // If we didn't get the DC from the binding (not sure why WinRT won't
            // carry this across), then just reach in and get it ourselves.
            if (this.DataContext == null)
            {
                var fe = sender as FrameworkElement;
                if (fe != null)
                    this.DataContext = fe.DataContext;
            }

            var thisCommand = this.Command;
            if (thisCommand != null)
            {
                var ep = new EventParameters(thisCommand, sender, e, this.CommandParameter);
                if (thisCommand.CanExecute(ep))
                    thisCommand.Execute(ep);
            }
            else
            {
                Debug.WriteLine(string.Format("Missing Command on event handler, {0}: Sender={1}, EventArgs={2}, DataContext={3}", this.Event, sender, e, this.DataContext));
            }
        }
    }

    /// <summary>
    /// Collection of command to event mappings
    /// </summary>
    public class CommandEventCollection : ObservableCollection<CommandEvent>
    {
        private object _target;
        private readonly List<CommandEvent> _currentList = new List<CommandEvent>();

        /// <summary>
        /// Constructor
        /// </summary>
        public CommandEventCollection()
        {
            ((INotifyCollectionChanged)this).CollectionChanged += this.OnCollectionChanged;
        }

        /// <summary>
        /// Wire up events to the target
        /// </summary>
        /// <param name="target"></param>
        internal void Subscribe(object target)
        {
            this._target = target;
            foreach (var item in this)
                item.Subscribe(target);
        }

        /// <summary>
        /// Unwire all target events
        /// </summary>
        /// <param name="target"></param>
        internal void Unsubscribe(object target)
        {
            foreach (var item in this)
                item.Unsubscribe(target);
            this._target = null;
        }

        /// <summary>
        /// This handles the collection change event - it then subscribes and unsubscribes events.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    foreach (var item in e.NewItems)
                        this.OnItemAdded((CommandEvent)item);
                    break;

                case NotifyCollectionChangedAction.Remove:
                    foreach (var item in e.OldItems)
                        this.OnItemRemoved((CommandEvent)item);
                    break;

                case NotifyCollectionChangedAction.Replace:
                    foreach (var item in e.OldItems)
                        this.OnItemRemoved((CommandEvent)item);
                    foreach (var item in e.NewItems)
                        this.OnItemAdded((CommandEvent)item);
                    break;

                case NotifyCollectionChangedAction.Move:
                    break;

                case NotifyCollectionChangedAction.Reset:
                    foreach (var item in this._currentList)
                        item.Unsubscribe(this._target);
                    this._currentList.Clear();
                    foreach (var item in this)
                        this.OnItemAdded(item);
                    break;

                default:
                    return;
            }
        }

        /// <summary>
        /// A new item has been added to the event list
        /// </summary>
        /// <param name="item"></param>
        private void OnItemAdded(CommandEvent item)
        {
            if (item != null && this._target != null)
            {
                this._currentList.Add(item);
                item.Subscribe(this._target);
            }
        }

        /// <summary>
        /// An item has been removed from the event list.
        /// </summary>
        /// <param name="item"></param>
        private void OnItemRemoved(CommandEvent item)
        {
            if (item != null && this._target != null)
            {
                this._currentList.Remove(item);
                item.Unsubscribe(this._target);
            }
        }
    }
}
