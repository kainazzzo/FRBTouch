﻿using System;
using System.Collections.Generic;
using System.Linq;
using FRBTouch.MultiTouch;
using Microsoft.Xna.Framework;

namespace FRBTouch
{
    public class GestureProvider
    {
        private readonly DateTime _startTime;
        private readonly ITouchEventProvider _touchEventProvider;
        private readonly Dictionary<int, TouchEvent> _touchPoints;
        private readonly Dictionary<int, TouchEvent> _movingPoints;
        private readonly Dictionary<int, PinchEventGroup> _pinchPoints;
        private readonly List<TouchEvent> _partialPinchComplete;
        private GestureSample? _lastTap;
        private TouchEvent _holdEvent;

        struct PinchEventGroup
        {
            public TouchEvent One { get; set; }
            public TouchEvent Two { get; set; }
        }

        public GestureProvider(ITouchEventProvider touchEventProvider)
        {
            _touchEventProvider = touchEventProvider;
            _startTime = DateTime.Now;
            _touchPoints = new Dictionary<int, TouchEvent>();
            _movingPoints = new Dictionary<int, TouchEvent>();
            _pinchPoints = new Dictionary<int, PinchEventGroup>();
            _partialPinchComplete = new List<TouchEvent>();
        }

        public IEnumerable<GestureSample> GetSamples()
        {
            var events = _touchEventProvider.Events;

            if (events == null || events.Count == 0)
            {
                return null;
            }

            var gestures = new List<GestureSample>();

            // First capture all the down touches so movement is easier to track
// ReSharper disable once ForCanBeConvertedToForeach
            for (var index = 0; index < events.Count; index++)
            {
                var touchEvent = events[index];
                if (touchEvent.Action == TouchEvent.TouchEventAction.Down &&
                    !_touchPoints.ContainsKey(touchEvent.Id))
                {
                    _touchPoints[touchEvent.Id] = touchEvent;
                    events.RemoveAt(index);
                    --index;
                }
            }

            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < events.Count; index++)
            {
                var touchEvent = events[index];

                if (touchEvent.Action == TouchEvent.TouchEventAction.Move)
                {
                    var originalEvent = _touchPoints[touchEvent.Id];

                    if (originalEvent.TranslatedPosition != touchEvent.TranslatedPosition)
                    {
                        _holdEvent = null;
                        _movingPoints[touchEvent.Id] = touchEvent;

                        #region pinch

                        if (_touchPoints.Count > 1)
                        {
                            // This was a pinch. Figure out if both points moved so we combine gestures

                            var pinchStart = new TouchEvent();
                            TouchEvent pinchMove = null;
                            foreach (var touchPoint in _touchPoints)
                            {
                                if (touchPoint.Key != touchEvent.Id)
                                {
                                    pinchStart = touchPoint.Value;
                                    break;
                                }
                            }

                            for (int index2 = index; index2 < events.Count; index2++)
                            {
                                var touchEvent2 = events[index2];
                                if (touchEvent2.Action == TouchEvent.TouchEventAction.Move &&
                                    touchEvent2.Id == pinchStart.Id)
                                {
                                    pinchMove = touchEvent2;
                                    _movingPoints[touchEvent2.Id] = touchEvent2;
                                    events.RemoveAt(index2);
                                    break;
                                }
                            }
                            if (pinchMove == null)
                            {
                                pinchMove = pinchStart;
                            }

                            _pinchPoints[touchEvent.Id]
                                = _pinchPoints[pinchMove.Id]
                                    = new PinchEventGroup
                                    {
                                        One = touchEvent,
                                        Two = pinchMove
                                    };

                            gestures.Add(new GestureSample(GestureType.Pinch,
                                touchEvent.TimeStamp - _startTime,
                                touchEvent.TranslatedPosition, pinchMove.TranslatedPosition,
                                touchEvent.TranslatedPosition - originalEvent.TranslatedPosition,
                                pinchMove.TranslatedPosition - pinchStart.TranslatedPosition,
                                touchEvent.NonTranslatedPosition, pinchMove.NonTranslatedPosition,
                                touchEvent.NonTranslatedPosition - originalEvent.NonTranslatedPosition,
                                pinchMove.NonTranslatedPosition - pinchStart.NonTranslatedPosition));

                            var originalCenter = (pinchStart.TranslatedPosition - originalEvent.TranslatedPosition)/2.0f;
                            var newCenter = (pinchMove.TranslatedPosition - touchEvent.TranslatedPosition)/2.0f;
                            var originalCenterNontranslated = (pinchStart.TranslatedPosition -
                                                               originalEvent.TranslatedPosition)/2.0f;
                            var newCenterNontranslated = (pinchMove.TranslatedPosition - touchEvent.TranslatedPosition)/
                                                         2.0f;

                            gestures.Add(new GestureSample(GestureType.FreeDrag,
                                touchEvent.TimeStamp - _startTime,
                                newCenter,
                                Vector2.Zero,
                                newCenter - originalCenter,
                                Vector2.Zero,
                                newCenterNontranslated,
                                Vector2.Zero,
                                newCenterNontranslated - originalCenterNontranslated,
                                Vector2.Zero));
                        }
                            #endregion

                            #region drag

                        else
                        {
                            gestures.Add(new GestureSample(GestureType.FreeDrag,
                                touchEvent.TimeStamp - _startTime,
                                touchEvent.TranslatedPosition,
                                Vector2.Zero,
                                touchEvent.TranslatedPosition - originalEvent.TranslatedPosition,
                                Vector2.Zero,
                                touchEvent.NonTranslatedPosition,
                                Vector2.Zero,
                                touchEvent.NonTranslatedPosition - originalEvent.NonTranslatedPosition,
                                Vector2.Zero));
                        }

                        #endregion
                    }
                    else if (_holdEvent == null &&
                             touchEvent.TimeStamp - originalEvent.TimeStamp >= TimeSpan.FromSeconds(1))
                    {
                        gestures.Add(new GestureSample(GestureType.Hold, touchEvent.TimeStamp - _startTime,
                            touchEvent.TranslatedPosition, Vector2.Zero, Vector2.Zero, Vector2.Zero,
                            touchEvent.NonTranslatedPosition, Vector2.Zero, Vector2.Zero, Vector2.Zero));
                        _holdEvent = touchEvent;
                    }
                }
                else if (touchEvent.Action == TouchEvent.TouchEventAction.Up)
                {
                    _touchPoints.Remove(touchEvent.Id);

                    if (_holdEvent != null)
                    {
                        _holdEvent = null;
                        continue;
                    }

                    if (!_movingPoints.ContainsKey(touchEvent.Id))
                    {
                        var tapGesture = new GestureSample(GestureType.Tap,
                            touchEvent.TimeStamp - _startTime,
                            touchEvent.TranslatedPosition,
                            Vector2.Zero,
                            Vector2.Zero,
                            Vector2.Zero,
                            touchEvent.NonTranslatedPosition,
                            Vector2.Zero,
                            Vector2.Zero,
                            Vector2.Zero);

                        if (!_lastTap.HasValue)
                        {
                            _lastTap = tapGesture;
                        }
                        else
                        {
                            if (tapGesture.Timestamp - _lastTap.Value.Timestamp > TimeSpan.FromMilliseconds(500))
                            {
                                _lastTap = tapGesture;
                            }
                            else
                            {
                                tapGesture.GestureType = GestureType.DoubleTap;
                            }
                        }

                        gestures.Add(tapGesture);
                    }
                    else
                    {
                        // The touch was moving, so it's a dragcomplete, but we only want to fire it once
                        // It could be a pinchcomplete too, so maybe i need to keep track of pinching points.
                        _movingPoints.Remove(touchEvent.Id);

                        if (_pinchPoints.ContainsKey(touchEvent.Id))
                        {
                            var pinchPoint = _pinchPoints[touchEvent.Id];
                            _pinchPoints.Remove(touchEvent.Id);

                            if (!_partialPinchComplete.Contains(touchEvent, TouchEvent.IdComparer))
                            {
                                var otherPoint = pinchPoint.One.Id == touchEvent.Id
                                    ? pinchPoint.Two
                                    : pinchPoint.One;
                                _partialPinchComplete.Add(otherPoint);

                                gestures.Add(new GestureSample(GestureType.PinchComplete,
                                    touchEvent.TimeStamp - _startTime,
                                    pinchPoint.One.TranslatedPosition,
                                    pinchPoint.Two.TranslatedPosition,
                                    Vector2.Zero,
                                    Vector2.Zero,
                                    pinchPoint.One.NonTranslatedPosition,
                                    pinchPoint.Two.NonTranslatedPosition,
                                    Vector2.Zero,
                                    Vector2.Zero));
                            }
                        }
                        else
                        {
                            gestures.Add(new GestureSample(GestureType.DragComplete,
                                touchEvent.TimeStamp - _startTime,
                                touchEvent.TranslatedPosition,
                                Vector2.Zero,
                                Vector2.Zero,
                                Vector2.Zero,
                                touchEvent.NonTranslatedPosition,
                                Vector2.Zero,
                                Vector2.Zero,
                                Vector2.Zero));
                        }
                    }
                }
            }

            return gestures;
        }

        private TouchEvent CloneEvent(TouchEvent touchEvent)
        {
            return new TouchEvent
            {
                Action = touchEvent.Action,
                Id = touchEvent.Id,
                TranslatedPosition = touchEvent.TranslatedPosition,
                TimeStamp = touchEvent.TimeStamp
            };
        }
    }
}