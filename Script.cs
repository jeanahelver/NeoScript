using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FrooxEngine;
using FrooxEngine.UIX;
using Jint;
using FrooxEngine.LogiX;
using Jint.Runtime.Interop;
using System.Reflection;
using FrooxEngine.LogiX.WorldModel;
using Esprima.Ast;
using Jint.Native;
using BaseX;
using System.Globalization;

namespace Script
{
    public static class SctiptHelper
    {
        public static Type MemberInnerType(this MemberInfo data)
        {
            switch (data.MemberType)
            {
                case MemberTypes.Field:
                    return ((FieldInfo)data).FieldType;
                case MemberTypes.Property:
                    return ((PropertyInfo)data).PropertyType;
                default:
                    break;
            }
            return null;
        }
    }

    [Category("Script")]
    public class ScriptNode : LogixNode, IScriptObjectProvider
    {
        public readonly ScriptBase scriptBase;

        public object[] ScriptObjects => Array.Empty<object>();
    }

    [Category("Script")]
    public class Script : Component, IScriptObjectProvider
    {
        public readonly ScriptBase scriptBase;

        public object[] ScriptObjects => Array.Empty<object>();
    }

    public interface IScriptObjectProvider
    {
        object[] ScriptObjects { get; }
    }

    public class MultiLineString : MemberEditor
    {
        public readonly Sync<string> Format;

        protected readonly SyncRef<TextEditor> _textEditor;

        protected readonly FieldDrive<string> _textDrive;

        protected readonly SyncRef<Button> _button;

        protected readonly SyncRef<Button> _resetButton;

        protected override void OnAwake()
        {
            base.OnAwake();
            _textDrive.SetupValueSetHook(TextChanged);
        }

        protected override void BuildUI(UIBuilder ui)
        {
            var startSize = ui.Style.MinHeight;
            ui.Style.MinHeight = 350;
            ui.ScrollArea(Alignment.TopLeft);
            TextField textField = ui.TextField("", undo: false, null, parseRTF: false);
            textField.Text.NullContent.Value = MemberEditor.NULL_STRING;
            _textEditor.Target = textField.Editor.Target;
            _textDrive.Target = textField.Text.Content;
            _button.Target = textField.Slot.GetComponentInChildren<Button>();
            textField.Text.HorizontalAlign.Value = CodeX.TextHorizontalAlignment.Left;
            textField.Text.VerticalAlign.Value = CodeX.TextVerticalAlignment.Top;
            textField.Text.Size.Value = 16.5f;
            textField.Text.AutoSize = false;
            ui.Style.FlexibleWidth = -1f;
            ui.Style.MinWidth = 24f;
            var trains = ui.Current.AttachComponent<ContentSizeFitter>();
            trains.HorizontalFit.Value = SizeFit.MinSize;
            trains.VerticalFit.Value = SizeFit.MinSize;
            ui.NestOut();
            ui.Style.MinHeight = startSize;
            LocaleString text = "∅";
            Button button = ui.Button(in text);
            button.Pressed.Target = OnReset;
            _resetButton.Target = button;
            ui.Style.MinWidth = -1f;
            textField.Editor.Target.EditingStarted.Target = EditingStarted;
            textField.Editor.Target.EditingChanged.Target = EditingChanged;
            textField.Editor.Target.EditingFinished.Target = EditingFinished;
        }

        protected override void OnChanges()
        {
            base.OnChanges();
            if (base.Accessor == null)
            {
                return;
            }

            try
            {
                if (_textDrive.IsLinkValid)
                {
                    _textDrive.Target.Value = PrimitiveToString(base.Accessor.TargetType, GetMemberValue());
                }

                if (_button.Target != null)
                {
                    Button target = _button.Target;
                    color color = base.FieldColor;
                    target.SetColors(in color);
                }

                if (_resetButton.Target != null)
                {
                    _resetButton.Target.Slot.ActiveSelf = base.Accessor.TargetType == typeof(string);
                }
            }
            catch (Exception)
            {
                UniLog.Error("Exception in OnChanges on PrimitiveMemberEditor. Format: " + Format.Value + ", Path: " + _path.Value + ", Target: " + (_target.Target?.GetType())?.ToString() + " - " + _target.Target);
            }
        }

        [SyncMethod]
        private void EditingStarted(TextEditor editor)
        {
            if (base.Accessor != null)
            {
                if (PrimitiveToString(base.Accessor.TargetType, GetMemberValue()) == null)
                {
                    _textEditor.Target.TargetString = "";
                }

                _textDrive.Target = null;
            }
        }

        [SyncMethod]
        private void EditingChanged(TextEditor editor)
        {
            if (Continuous.Value)
            {
                ParseAndAssign();
            }
        }

        private void TextChanged(IField<string> field, string value)
        {
            _textEditor.Target.Text.Target.Text = value;
            ParseAndAssign();
        }

        [SyncMethod]
        private void EditingFinished(TextEditor editor)
        {
            ParseAndAssign();
            _textDrive.Target = ((Text)_textEditor.Target.Text.Target).Content;
        }

        [SyncMethod]
        private void OnReset(IButton button, ButtonEventData eventData)
        {
            StructFieldAccessor structFieldAccessor = base.Accessor;
            if (structFieldAccessor != null)
            {
                SetMemberValue((structFieldAccessor.TargetType ?? structFieldAccessor.RootType).GetDefaultValue());
            }
        }

        private void ParseAndAssign()
        {
            try
            {
                StructFieldAccessor structFieldAccessor = base.Accessor;
                if (structFieldAccessor != null)
                {
                    object parsed;
                    bool flag = PrimitiveTryParsers.GetParser(structFieldAccessor.TargetType)(_textEditor.Target.Text.Target.Text, out parsed);
                    Type type = parsed as Type;
                    if ((object)type != null && !WorkerManager.IsValidGenericType(type, validForInstantiation: false))
                    {
                        flag = false;
                        parsed = null;
                    }

                    if (flag)
                    {
                        SetMemberValue(parsed);
                    }
                }
            }
            catch (Exception arg)
            {
                UniLog.Error($"Exception assigning value to {_textDrive.Target}:\n{arg}");
            }
        }

        private string PrimitiveToString(Type primitiveType, object primitive)
        {
            if (primitive == null)
            {
                return null;
            }

            return primitiveType.Name switch
            {
                "Boolean" => ((bool)primitive).ToString(CultureInfo.InvariantCulture),
                "SByte" => ((sbyte)primitive).ToString(Format.Value, CultureInfo.InvariantCulture),
                "Int16" => ((short)primitive).ToString(Format.Value, CultureInfo.InvariantCulture),
                "Int32" => ((int)primitive).ToString(Format.Value, CultureInfo.InvariantCulture),
                "Int64" => ((long)primitive).ToString(Format.Value, CultureInfo.InvariantCulture),
                "Byte" => ((byte)primitive).ToString(Format.Value, CultureInfo.InvariantCulture),
                "UInt16" => ((ushort)primitive).ToString(Format.Value, CultureInfo.InvariantCulture),
                "UInt32" => ((uint)primitive).ToString(Format.Value, CultureInfo.InvariantCulture),
                "UInt64" => ((ulong)primitive).ToString(Format.Value, CultureInfo.InvariantCulture),
                "Single" => ((float)primitive).ToString(Format.Value, CultureInfo.InvariantCulture),
                "Double" => ((double)primitive).ToString(Format.Value, CultureInfo.InvariantCulture),
                "Decimal" => ((decimal)primitive).ToString(Format.Value, CultureInfo.InvariantCulture),
                "Type" => (primitive as Type)?.FullName,
                _ => primitive.ToString(),
            };
        }

    }

    public class ScriptBase : SyncObject, ICustomInspector
    {
        public Jint.Engine jsEngine;

        [DefaultValue(@"
function Code()	{

}
")]
        public readonly Sync<string> Script;


        protected override void OnAwake()
        {
            base.OnAwake();
            Script.Changed += Script_Changed;
            Script_Changed(null);
        }

        private void Script_Changed(IChangeable obj)
        {
            jsEngine?.Dispose();
            jsEngine = null;
            jsEngine = new Jint.Engine(options =>
            {
                options.TimeoutInterval(TimeSpan.FromSeconds(1));
                options.MaxStatements(1580);
                options.SetTypeResolver(new TypeResolver
                {
                    MemberFilter = member => (Attribute.IsDefined(member, typeof(SyncMethod)) || typeof(IWorldElement).IsAssignableFrom(member.MemberInnerType())),
                });
                options.Strict = true;
            });
            jsEngine.SetValue("script", this);
            jsEngine.SetValue("slot", this.FindNearestParent<Slot>());
            jsEngine.SetValue("world", World);
            jsEngine.SetValue("localUser", LocalUser);
            jsEngine.SetValue("localUserRoot", LocalUserRoot);
            try
            {
                jsEngine.Execute(Script);

            }
            catch (Exception ex)
            {
                jsEngine = null;
                Debug.Log("Script Err " + ex.ToString());
            }
        }

        public void BuildInspectorUI(UIBuilder ui)
        {
            ui.MemberEditor<MultiLineString>(Script);
            ui.Button("Run Script", ButtonRun);
        }


        [SyncMethod]
        public void ButtonRun(IButton button, ButtonEventData buttonEventData)
        {
            button.Enabled = false;
            RunScript();
            button.Enabled = true;
        }

        [ImpulseTarget]
        public void RunScript()
        {
            if (jsEngine == null)
                return;
            try
            {
                if (jsEngine.GetValue("Code") == JsValue.Undefined)
                {
                    throw new Exception("function Code Not found");
                }
                IScriptObjectProvider scriptObjectProvider = null;
                if (this.Parent is IScriptObjectProvider scriptData)
                {
                    scriptObjectProvider = scriptData;
                }
                jsEngine.Invoke("Code", scriptObjectProvider?.ScriptObjects ?? Array.Empty<object>());
            }
            catch (Exception ex)
            {
                Debug.Log("Script code run Err " + ex.ToString());
            }
        }
    }
}
