﻿using FlysasLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace FlysasClient
{

    [AttributeUsage(AttributeTargets.Property, Inherited = false)]
    public class OptionParserAttribute : Attribute
    {
        public string OptionName { get; private set; }
        public bool Secret { get; private set; } = false;
        public OptionParserAttribute(string optionName, bool Secret = false)
        {
            this.OptionName = optionName;
            this.Secret = Secret;
        }
    }

    public abstract class OptionsParser
    {
        public OptionsParser(IEnumerable<KeyValuePair<string, string>> options)
        {
            foreach (var option in options)
                if (!option.Value.IsNullOrWhiteSpace())
                    mySet(option.Key, option.Value);
        }

        private bool mySet(string option, string value)
        {
            foreach (var prop in this.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = prop.GetCustomAttribute(typeof(OptionParserAttribute)) as OptionParserAttribute;
                if (attr != null && attr.OptionName.Equals(option, StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.PropertyType == typeof(bool))
                        prop.SetValue(this, myBool(value));
                    if (prop.PropertyType == typeof(string))
                        prop.SetValue(this, value);
                    if (prop.PropertyType == typeof(int))
                    {
                        int intVal;
                        if (int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out intVal))
                            prop.SetValue(this, intVal);
                        else
                            return false;
                    }
                    if (prop.PropertyType.IsEnum)
                    {
                        object enumVal = null;
                        if (Enum.TryParse(prop.PropertyType, value, true, out enumVal))
                            prop.SetValue(this, enumVal);
                        else
                            return false;
                    }
                    return true;
                }
            }
            return false;
        }

        public string Help()
        {
            string s = "";
            foreach (var prop in this.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = prop.GetCustomAttribute(typeof(OptionParserAttribute)) as OptionParserAttribute;
                if (attr != null)
                {
                    s += attr.OptionName + ":" + (attr.Secret ? "************" : prop.GetValue(this)?.ToString()) + " ";
                }
            }
            return s;
        }

        public bool Set(Stack<string> stack)
        {
            if (stack.Any())
            {
                var allGood = true;
                while (stack.Count >= 2)
                    allGood &= mySet(stack.Pop(), stack.Pop());
                return allGood;
            }
            return false;
        }

        bool myBool(string val)
        {
            string s = val?.ToLower();
            return s == "on" || s == "true" || s == "1" || s == "yes";
        }
    }
    public class Options : OptionsParser
    {
        [OptionParser("days")]
        public int Days { get; private set; } = 1;
        [OptionParser("bookingclass")]
        public bool OutputBookingClass { get; private set; } = false;
        [OptionParser("equipment")]
        public bool OutputEquipment { get; private set; } = false;
        [OptionParser("username")]
        public string UserName { get; set; }
        [OptionParser(optionName: "passWord", Secret: true)]
        public string Password { get; set; }
        [OptionParser("flightnumber")]
        public bool OutputFlightNumber { get; private set; } = false;
        [OptionParser("mode")]
        public SASQuery.SearhMode Mode { get; private set; } = SASQuery.SearhMode.REVENUE;
        public Options(IEnumerable<KeyValuePair<string, string>> options) : base(options) { }
    }
}
