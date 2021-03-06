// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text;

namespace System
{
    public sealed partial class TimeZoneInfo
    {
        /// <summary>
        /// This class is used to serialize and deserialize TimeZoneInfo
        /// objects based on the custom string serialization format.
        /// </summary>
        private sealed class StringSerializer
        {
            private enum State
            {
                Escaped = 0,
                NotEscaped = 1,
                StartOfToken = 2,
                EndOfLine = 3
            }

            private readonly string _serializedText;
            private int _currentTokenStartIndex;
            private State _state;

            // the majority of the strings contained in the OS time zones fit in 64 chars
            private const int InitialCapacityForString = 64;
            private const char Esc = '\\';
            private const char Sep = ';';
            private const char Lhs = '[';
            private const char Rhs = ']';
            private const string EscString = "\\";
            private const string SepString = ";";
            private const string LhsString = "[";
            private const string RhsString = "]";
            private const string EscapedEsc = "\\\\";
            private const string EscapedSep = "\\;";
            private const string EscapedLhs = "\\[";
            private const string EscapedRhs = "\\]";
            private const string DateTimeFormat = "MM:dd:yyyy";
            private const string TimeOfDayFormat = "HH:mm:ss.FFF";

            /// <summary>
            /// Creates the custom serialized string representation of a TimeZoneInfo instance.
            /// </summary>
            public static string GetSerializedString(TimeZoneInfo zone)
            {
                StringBuilder serializedText = StringBuilderCache.Acquire();

                //
                // <_id>;<_baseUtcOffset>;<_displayName>;<_standardDisplayName>;<_daylightDispayName>
                //
                serializedText.Append(SerializeSubstitute(zone.Id));
                serializedText.Append(Sep);
                serializedText.Append(SerializeSubstitute(
                           zone.BaseUtcOffset.TotalMinutes.ToString(CultureInfo.InvariantCulture)));
                serializedText.Append(Sep);
                serializedText.Append(SerializeSubstitute(zone.DisplayName));
                serializedText.Append(Sep);
                serializedText.Append(SerializeSubstitute(zone.StandardName));
                serializedText.Append(Sep);
                serializedText.Append(SerializeSubstitute(zone.DaylightName));
                serializedText.Append(Sep);

                AdjustmentRule[] rules = zone.GetAdjustmentRules();

                if (rules != null && rules.Length > 0)
                {
                    foreach (AdjustmentRule rule in rules)
                    {
                        serializedText.Append(Lhs);
                        serializedText.Append(SerializeSubstitute(rule.DateStart.ToString(DateTimeFormat, DateTimeFormatInfo.InvariantInfo)));
                        serializedText.Append(Sep);
                        serializedText.Append(SerializeSubstitute(rule.DateEnd.ToString(DateTimeFormat, DateTimeFormatInfo.InvariantInfo)));
                        serializedText.Append(Sep);
                        serializedText.Append(SerializeSubstitute(rule.DaylightDelta.TotalMinutes.ToString(CultureInfo.InvariantCulture)));
                        serializedText.Append(Sep);
                        // serialize the TransitionTime's
                        SerializeTransitionTime(rule.DaylightTransitionStart, serializedText);
                        serializedText.Append(Sep);
                        SerializeTransitionTime(rule.DaylightTransitionEnd, serializedText);
                        serializedText.Append(Sep);
                        if (rule.BaseUtcOffsetDelta != TimeSpan.Zero)
                        {
                            // Serialize it only when BaseUtcOffsetDelta has a value to reduce the impact of adding rule.BaseUtcOffsetDelta
                            serializedText.Append(SerializeSubstitute(rule.BaseUtcOffsetDelta.TotalMinutes.ToString(CultureInfo.InvariantCulture)));
                            serializedText.Append(Sep);
                        }
                        if (rule.NoDaylightTransitions)
                        {
                            // Serialize it only when NoDaylightTransitions is true to reduce the impact of adding rule.NoDaylightTransitions
                            serializedText.Append(SerializeSubstitute("1"));
                            serializedText.Append(Sep);
                        }
                        serializedText.Append(Rhs);
                    }
                }
                serializedText.Append(Sep);
                return StringBuilderCache.GetStringAndRelease(serializedText);
            }

            /// <summary>
            /// Instantiates a TimeZoneInfo from a custom serialized string.
            /// </summary>
            public static TimeZoneInfo GetDeserializedTimeZoneInfo(string source)
            {
                StringSerializer s = new StringSerializer(source);

                string id = s.GetNextStringValue(canEndWithoutSeparator: false);
                TimeSpan baseUtcOffset = s.GetNextTimeSpanValue(canEndWithoutSeparator: false);
                string displayName = s.GetNextStringValue(canEndWithoutSeparator: false);
                string standardName = s.GetNextStringValue(canEndWithoutSeparator: false);
                string daylightName = s.GetNextStringValue(canEndWithoutSeparator: false);
                AdjustmentRule[] rules = s.GetNextAdjustmentRuleArrayValue(canEndWithoutSeparator: false);

                try
                {
                    return new TimeZoneInfo(id, baseUtcOffset, displayName, standardName, daylightName, rules, disableDaylightSavingTime: false);
                }
                catch (ArgumentException ex)
                {
                    throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"), ex);
                }
                catch (InvalidTimeZoneException ex)
                {
                    throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"), ex);
                }
            }

            private StringSerializer(string str)
            {
                _serializedText = str;
                _state = State.StartOfToken;
            }

            /// <summary>
            /// Returns a new string with all of the reserved sub-strings escaped
            ///
            /// ";" -> "\;"
            /// "[" -> "\["
            /// "]" -> "\]"
            /// "\" -> "\\"
            /// </summary>
            private static string SerializeSubstitute(string text)
            {
                text = text.Replace(EscString, EscapedEsc);
                text = text.Replace(LhsString, EscapedLhs);
                text = text.Replace(RhsString, EscapedRhs);
                return text.Replace(SepString, EscapedSep);
            }

            /// <summary>
            /// Helper method to serialize a TimeZoneInfo.TransitionTime object.
            /// </summary>
            private static void SerializeTransitionTime(TransitionTime time, StringBuilder serializedText)
            {
                serializedText.Append(Lhs);
                int fixedDate = (time.IsFixedDateRule ? 1 : 0);
                serializedText.Append(fixedDate.ToString(CultureInfo.InvariantCulture));
                serializedText.Append(Sep);

                if (time.IsFixedDateRule)
                {
                    serializedText.Append(SerializeSubstitute(time.TimeOfDay.ToString(TimeOfDayFormat, DateTimeFormatInfo.InvariantInfo)));
                    serializedText.Append(Sep);
                    serializedText.Append(SerializeSubstitute(time.Month.ToString(CultureInfo.InvariantCulture)));
                    serializedText.Append(Sep);
                    serializedText.Append(SerializeSubstitute(time.Day.ToString(CultureInfo.InvariantCulture)));
                    serializedText.Append(Sep);
                }
                else
                {
                    serializedText.Append(SerializeSubstitute(time.TimeOfDay.ToString(TimeOfDayFormat, DateTimeFormatInfo.InvariantInfo)));
                    serializedText.Append(Sep);
                    serializedText.Append(SerializeSubstitute(time.Month.ToString(CultureInfo.InvariantCulture)));
                    serializedText.Append(Sep);
                    serializedText.Append(SerializeSubstitute(time.Week.ToString(CultureInfo.InvariantCulture)));
                    serializedText.Append(Sep);
                    serializedText.Append(SerializeSubstitute(((int)time.DayOfWeek).ToString(CultureInfo.InvariantCulture)));
                    serializedText.Append(Sep);
                }
                serializedText.Append(Rhs);
            }

            /// <summary>
            /// Helper function to determine if the passed in string token is allowed to be preceeded by an escape sequence token.
            /// </summary>
            private static void VerifyIsEscapableCharacter(char c)
            {
                if (c != Esc && c != Sep && c != Lhs && c != Rhs)
                {
                    throw new SerializationException(Environment.GetResourceString("Serialization_InvalidEscapeSequence", c));
                }
            }

            /// <summary>
            /// Helper function that reads past "v.Next" data fields. Receives a "depth" parameter indicating the
            /// current relative nested bracket depth that _currentTokenStartIndex is at. The function ends
            /// successfully when "depth" returns to zero (0).
            /// </summary>
            private void SkipVersionNextDataFields(int depth /* starting depth in the nested brackets ('[', ']')*/)
            {
                if (_currentTokenStartIndex < 0 || _currentTokenStartIndex >= _serializedText.Length)
                {
                    throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));
                }
                State tokenState = State.NotEscaped;

                // walk the serialized text, building up the token as we go...
                for (int i = _currentTokenStartIndex; i < _serializedText.Length; i++)
                {
                    if (tokenState == State.Escaped)
                    {
                        VerifyIsEscapableCharacter(_serializedText[i]);
                        tokenState = State.NotEscaped;
                    }
                    else if (tokenState == State.NotEscaped)
                    {
                        switch (_serializedText[i])
                        {
                            case Esc:
                                tokenState = State.Escaped;
                                break;

                            case Lhs:
                                depth++;
                                break;
                            case Rhs:
                                depth--;
                                if (depth == 0)
                                {
                                    _currentTokenStartIndex = i + 1;
                                    if (_currentTokenStartIndex >= _serializedText.Length)
                                    {
                                        _state = State.EndOfLine;
                                    }
                                    else
                                    {
                                        _state = State.StartOfToken;
                                    }
                                    return;
                                }
                                break;

                            case '\0':
                                // invalid character
                                throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));

                            default:
                                break;
                        }
                    }
                }

                throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));
            }

            /// <summary>
            /// Helper function that reads a string token from the serialized text. The function
            /// updates <see cref="_currentTokenStartIndex"/> to point to the next token on exit.
            /// Also <see cref="_state"/> is set to either <see cref="State.StartOfToken"/> or
            /// <see cref="State.EndOfLine"/> on exit.
            /// </summary>
            /// <param name="canEndWithoutSeparator">
            /// - When set to 'false' the function requires the string token end with a ";".
            /// - When set to 'true' the function requires that the string token end with either
            ///   ";", <see cref="State.EndOfLine"/>, or "]". In the case that "]" is the terminal
            ///   case the <see cref="_currentTokenStartIndex"/> is left pointing at index "]" to
            ///   allow the caller to update its depth logic.
            /// </param>
            private string GetNextStringValue(bool canEndWithoutSeparator)
            {
                // first verify the internal state of the object
                if (_state == State.EndOfLine)
                {
                    if (canEndWithoutSeparator)
                    {
                        return null;
                    }
                    else
                    {
                        throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));
                    }
                }
                if (_currentTokenStartIndex < 0 || _currentTokenStartIndex >= _serializedText.Length)
                {
                    throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));
                }
                State tokenState = State.NotEscaped;
                StringBuilder token = StringBuilderCache.Acquire(InitialCapacityForString);

                // walk the serialized text, building up the token as we go...
                for (int i = _currentTokenStartIndex; i < _serializedText.Length; i++)
                {
                    if (tokenState == State.Escaped)
                    {
                        VerifyIsEscapableCharacter(_serializedText[i]);
                        token.Append(_serializedText[i]);
                        tokenState = State.NotEscaped;
                    }
                    else if (tokenState == State.NotEscaped)
                    {
                        switch (_serializedText[i])
                        {
                            case Esc:
                                tokenState = State.Escaped;
                                break;

                            case Lhs:
                                // '[' is an unexpected character
                                throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));

                            case Rhs:
                                if (canEndWithoutSeparator)
                                {
                                    // if ';' is not a required terminal then treat ']' as a terminal
                                    // leave _currentTokenStartIndex pointing to ']' so our callers can handle
                                    // this special case
                                    _currentTokenStartIndex = i;
                                    _state = State.StartOfToken;
                                    return token.ToString();
                                }
                                else
                                {
                                    // ']' is an unexpected character
                                    throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));
                                }

                            case Sep:
                                _currentTokenStartIndex = i + 1;
                                if (_currentTokenStartIndex >= _serializedText.Length)
                                {
                                    _state = State.EndOfLine;
                                }
                                else
                                {
                                    _state = State.StartOfToken;
                                }
                                return StringBuilderCache.GetStringAndRelease(token);

                            case '\0':
                                // invalid character
                                throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));

                            default:
                                token.Append(_serializedText[i]);
                                break;
                        }
                    }
                }
                //
                // we are at the end of the line
                //
                if (tokenState == State.Escaped)
                {
                    // we are at the end of the serialized text but we are in an escaped state
                    throw new SerializationException(Environment.GetResourceString("Serialization_InvalidEscapeSequence", string.Empty));
                }

                if (!canEndWithoutSeparator)
                {
                    throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));
                }
                _currentTokenStartIndex = _serializedText.Length;
                _state = State.EndOfLine;
                return StringBuilderCache.GetStringAndRelease(token);
            }

            /// <summary>
            /// Helper function to read a DateTime token.
            /// </summary>
            private DateTime GetNextDateTimeValue(bool canEndWithoutSeparator, string format)
            {
                string token = GetNextStringValue(canEndWithoutSeparator);
                DateTime time;
                if (!DateTime.TryParseExact(token, format, DateTimeFormatInfo.InvariantInfo, DateTimeStyles.None, out time))
                {
                    throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));
                }
                return time;
            }

            /// <summary>
            /// Helper function to read a TimeSpan token.
            /// </summary>
            private TimeSpan GetNextTimeSpanValue(bool canEndWithoutSeparator)
            {
                int token = GetNextInt32Value(canEndWithoutSeparator);
                try
                {
                    return new TimeSpan(hours: 0, minutes: token, seconds: 0);
                }
                catch (ArgumentOutOfRangeException e)
                {
                    throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"), e);
                }
            }

            /// <summary>
            /// Helper function to read an Int32 token.
            /// </summary>
            private int GetNextInt32Value(bool canEndWithoutSeparator)
            {
                string token = GetNextStringValue(canEndWithoutSeparator);
                int value;
                if (!int.TryParse(token, NumberStyles.AllowLeadingSign /* "[sign]digits" */, CultureInfo.InvariantCulture, out value))
                {
                    throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));
                }
                return value;
            }

            /// <summary>
            /// Helper function to read an AdjustmentRule[] token.
            /// </summary>
            private AdjustmentRule[] GetNextAdjustmentRuleArrayValue(bool canEndWithoutSeparator)
            {
                List<AdjustmentRule> rules = new List<AdjustmentRule>(1);
                int count = 0;

                // individual AdjustmentRule array elements do not require semicolons
                AdjustmentRule rule = GetNextAdjustmentRuleValue(canEndWithoutSeparator: true);
                while (rule != null)
                {
                    rules.Add(rule);
                    count++;

                    rule = GetNextAdjustmentRuleValue(canEndWithoutSeparator: true);
                }

                if (!canEndWithoutSeparator)
                {
                    // the AdjustmentRule array must end with a separator
                    if (_state == State.EndOfLine)
                    {
                        throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));
                    }
                    if (_currentTokenStartIndex < 0 || _currentTokenStartIndex >= _serializedText.Length)
                    {
                        throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));
                    }
                }

                return count != 0 ? rules.ToArray() : null;
            }

            /// <summary>
            /// Helper function to read an AdjustmentRule token.
            /// </summary>
            private AdjustmentRule GetNextAdjustmentRuleValue(bool canEndWithoutSeparator)
            {
                // first verify the internal state of the object
                if (_state == State.EndOfLine)
                {
                    if (canEndWithoutSeparator)
                    {
                        return null;
                    }
                    else
                    {
                        throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));
                    }
                }

                if (_currentTokenStartIndex < 0 || _currentTokenStartIndex >= _serializedText.Length)
                {
                    throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));
                }

                // check to see if the very first token we see is the separator
                if (_serializedText[_currentTokenStartIndex] == Sep)
                {
                    return null;
                }

                // verify the current token is a left-hand-side marker ("[")
                if (_serializedText[_currentTokenStartIndex] != Lhs)
                {
                    throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));
                }
                _currentTokenStartIndex++;

                DateTime dateStart = GetNextDateTimeValue(false, DateTimeFormat);
                DateTime dateEnd = GetNextDateTimeValue(false, DateTimeFormat);
                TimeSpan daylightDelta = GetNextTimeSpanValue(canEndWithoutSeparator: false);
                TransitionTime daylightStart = GetNextTransitionTimeValue(canEndWithoutSeparator: false);
                TransitionTime daylightEnd = GetNextTransitionTimeValue(canEndWithoutSeparator: false);
                TimeSpan baseUtcOffsetDelta = TimeSpan.Zero;
                int noDaylightTransitions = 0;

                // verify that the string is now at the right-hand-side marker ("]") ...

                if (_state == State.EndOfLine || _currentTokenStartIndex >= _serializedText.Length)
                {
                    throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));
                }

                // Check if we have baseUtcOffsetDelta in the serialized string and then deserialize it
                if ((_serializedText[_currentTokenStartIndex] >= '0' && _serializedText[_currentTokenStartIndex] <= '9') ||
                    _serializedText[_currentTokenStartIndex] == '-' || _serializedText[_currentTokenStartIndex] == '+')
                {
                    baseUtcOffsetDelta = GetNextTimeSpanValue(canEndWithoutSeparator: false);
                }

                // Check if we have NoDaylightTransitions in the serialized string and then deserialize it
                if ((_serializedText[_currentTokenStartIndex] >= '0' && _serializedText[_currentTokenStartIndex] <= '1'))
                {
                    noDaylightTransitions = GetNextInt32Value(canEndWithoutSeparator: false);
                }

                if (_state == State.EndOfLine || _currentTokenStartIndex >= _serializedText.Length)
                {
                    throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));
                }

                if (_serializedText[_currentTokenStartIndex] != Rhs)
                {
                    // skip ahead of any "v.Next" data at the end of the AdjustmentRule
                    //
                    // FUTURE: if the serialization format is extended in the future then this
                    // code section will need to be changed to read the new fields rather
                    // than just skipping the data at the end of the [AdjustmentRule].
                    SkipVersionNextDataFields(1);
                }
                else
                {
                    _currentTokenStartIndex++;
                }

                // create the AdjustmentRule from the deserialized fields ...

                AdjustmentRule rule;
                try
                {
                    rule = AdjustmentRule.CreateAdjustmentRule(dateStart, dateEnd, daylightDelta, daylightStart, daylightEnd, baseUtcOffsetDelta, noDaylightTransitions > 0);
                }
                catch (ArgumentException e)
                {
                    throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"), e);
                }

                // finally set the state to either EndOfLine or StartOfToken for the next caller
                if (_currentTokenStartIndex >= _serializedText.Length)
                {
                    _state = State.EndOfLine;
                }
                else
                {
                    _state = State.StartOfToken;
                }
                return rule;
            }

            /// <summary>
            /// Helper function to read a TransitionTime token.
            /// </summary>
            private TransitionTime GetNextTransitionTimeValue(bool canEndWithoutSeparator)
            {
                // first verify the internal state of the object

                if (_state == State.EndOfLine ||
                    (_currentTokenStartIndex < _serializedText.Length && _serializedText[_currentTokenStartIndex] == Rhs))
                {
                    //
                    // we are at the end of the line or we are starting at a "]" character
                    //
                    throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));
                }

                if (_currentTokenStartIndex < 0 || _currentTokenStartIndex >= _serializedText.Length)
                {
                    throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));
                }

                // verify the current token is a left-hand-side marker ("[")

                if (_serializedText[_currentTokenStartIndex] != Lhs)
                {
                    throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));
                }
                _currentTokenStartIndex++;

                int isFixedDate = GetNextInt32Value(canEndWithoutSeparator: false);

                if (isFixedDate != 0 && isFixedDate != 1)
                {
                    throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));
                }

                TransitionTime transition;

                DateTime timeOfDay = GetNextDateTimeValue(false, TimeOfDayFormat);
                timeOfDay = new DateTime(1, 1, 1, timeOfDay.Hour, timeOfDay.Minute, timeOfDay.Second, timeOfDay.Millisecond);

                int month = GetNextInt32Value(canEndWithoutSeparator: false);

                if (isFixedDate == 1)
                {
                    int day = GetNextInt32Value(canEndWithoutSeparator: false);

                    try
                    {
                        transition = TransitionTime.CreateFixedDateRule(timeOfDay, month, day);
                    }
                    catch (ArgumentException e)
                    {
                        throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"), e);
                    }
                }
                else
                {
                    int week = GetNextInt32Value(canEndWithoutSeparator: false);
                    int dayOfWeek = GetNextInt32Value(canEndWithoutSeparator: false);

                    try
                    {
                        transition = TransitionTime.CreateFloatingDateRule(timeOfDay, month, week, (DayOfWeek)dayOfWeek);
                    }
                    catch (ArgumentException e)
                    {
                        throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"), e);
                    }
                }

                // verify that the string is now at the right-hand-side marker ("]") ...

                if (_state == State.EndOfLine || _currentTokenStartIndex >= _serializedText.Length)
                {
                    throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));
                }

                if (_serializedText[_currentTokenStartIndex] != Rhs)
                {
                    // skip ahead of any "v.Next" data at the end of the AdjustmentRule
                    //
                    // FUTURE: if the serialization format is extended in the future then this
                    // code section will need to be changed to read the new fields rather
                    // than just skipping the data at the end of the [TransitionTime].
                    SkipVersionNextDataFields(1);
                }
                else
                {
                    _currentTokenStartIndex++;
                }

                // check to see if the string is now at the separator (";") ...
                bool sepFound = false;
                if (_currentTokenStartIndex < _serializedText.Length &&
                    _serializedText[_currentTokenStartIndex] == Sep)
                {
                    // handle the case where we ended on a ";"
                    _currentTokenStartIndex++;
                    sepFound = true;
                }

                if (!sepFound && !canEndWithoutSeparator)
                {
                    // we MUST end on a separator
                    throw new SerializationException(Environment.GetResourceString("Serialization_InvalidData"));
                }

                // finally set the state to either EndOfLine or StartOfToken for the next caller
                if (_currentTokenStartIndex >= _serializedText.Length)
                {
                    _state = State.EndOfLine;
                }
                else
                {
                    _state = State.StartOfToken;
                }
                return transition;
            }
        }
    }
}
