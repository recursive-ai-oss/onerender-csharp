namespace OneRender.Forms;

/// <summary>
/// The style sheet that gives form controls the look and box model Chrome 156 gives them by default. The values are the controls'
/// computed styles, read back from Chrome with <c>tools/forms/probe.sh</c> (see <c>docs/forms.md</c>); the family's user-agent sheet
/// carries only the HTML rendering section's suggestions. It is applied as a user-origin sheet, so author rules override it.
/// </summary>
internal static class FormDefaults
{
    public const string Css =
        "input,textarea,select,button{font:13.3333px Arial;margin:0;}" +
        // Chrome 156: fields and drop-downs take FieldText, buttons ButtonText (system colors, so they follow the color scheme).
        "input,textarea,select{color:FieldText;}" +
        "button,input:is([type=button i],[type=submit i],[type=reset i]){color:ButtonText;}" +
        "textarea{font-family:monospace;}" +
        "textarea,select{display:inline-block;}" +
        "input,textarea{box-sizing:content-box;}" +
        "input:is([type=radio i],[type=checkbox i],[type=reset i],[type=button i],[type=submit i],[type=color i],[type=search i]),select,button{box-sizing:border-box;}" +
        "input{padding:1px 2px;border:2px inset;border-color:light-dark(#767676,#858585);background-color:Field;}" +
        "input:is([type=checkbox i],[type=radio i]){padding:0;border:0 none;background-color:transparent;margin:3px 3px 3px 4px;}" +
        "input[type=radio i]{margin:3px 3px 0 5px;}" +
        "input[type=range i]{padding:0;border:0 none;background-color:transparent;margin:2px;}" +
        "input:is([type=image i],[type=file i]){padding:0;border:0 none;background-color:transparent;}" +
        "input:is([type=button i],[type=submit i],[type=reset i]),button{padding:1px 6px;border:2px outset;border-color:light-dark(#767676,#6b6b6b);background-color:ButtonFace;text-align:center;}" +
        "textarea{padding:2px;border:1px solid;border-color:light-dark(#767676,#858585);background-color:Field;overflow:auto;white-space:pre-wrap;}" +
        "select{padding:0;border:1px solid;border-color:light-dark(#767676,#858585);background-color:ButtonFace;white-space:pre;text-align:start;overflow:clip;}" +
        "select:is([multiple],[size]:not([size=\"0\"],[size=\"1\"])){background-color:Field;vertical-align:text-bottom;white-space:nowrap;overflow:hidden;}" +
        // The HTML standard's customizable-select rules in the family sheet (rounded corners, 24 px minimums, hover tints, flex options)
        // are not what a default select looks like in Chrome: put the classic box model back.
        "select{border-radius:0;min-width:0;min-height:0;gap:0;}" +
        "select:enabled:hover,select:enabled:active{background-color:ButtonFace;}" +
        "select:is([multiple],[size]:not([size=\"0\"],[size=\"1\"])):enabled:hover{background-color:Field;}" +
        "option{display:block;padding:0 2px 1px;white-space:nowrap;min-width:0;min-height:1.2em;align-items:normal;gap:0;}" +
        "option:enabled:hover,option:enabled:active{background-color:transparent;}" +
        "optgroup{display:block;font-weight:bold;}" +
        "progress{display:inline-block;box-sizing:border-box;width:10em;height:1em;vertical-align:-0.2em;}" +
        "meter{display:inline-block;box-sizing:border-box;width:5em;height:1em;vertical-align:-0.2em;}" +
        "fieldset{border-color:light-dark(#efefef,#bfbfbf);}" +
        "fieldset>legend{width:fit-content;}" +
        "input:is([type=checkbox i],[type=radio i]):focus-visible{outline-offset:2px;}" +
        "button:enabled:hover,input:is([type=button i],[type=submit i],[type=reset i]):enabled:hover{background-color:light-dark(#e5e5e5,#6b6b6b);border-color:light-dark(#4f4f4f,#6b6b6b);}" +
        "button:enabled:active,input:is([type=button i],[type=submit i],[type=reset i]):enabled:active{background-color:light-dark(#f5f5f5,#616161);border-color:light-dark(#464646,#616161);}" +
        // Disabled controls. The dark values are what Chrome 156 paints (opaque faces; its computed styles carry translucent ones).
        "input:disabled,textarea:disabled,select:disabled{color:light-dark(#545454,#aaa);}" +
        "input:disabled:not([type=checkbox i],[type=radio i],[type=range i],[type=button i],[type=submit i],[type=reset i]),textarea:disabled{background-color:light-dark(rgba(239,239,239,.3),#3b3b3b);border-color:light-dark(rgba(118,118,118,.3),#626262);}" +
        "button:disabled,input:is([type=button i],[type=submit i],[type=reset i]):disabled{color:light-dark(rgba(16,16,16,.35),rgba(255,255,255,.3));background-color:light-dark(#eee,#363636);border-color:light-dark(rgba(118,118,118,.3),#363636);}" +
        "select:disabled{color:light-dark(rgba(16,16,16,.38),rgba(255,255,255,.38));background-color:light-dark(rgba(239,239,239,.3),#2e2e2e);border-color:light-dark(rgba(118,118,118,.3),#4a4a4a);}" +
        // A disabled list box keeps the field face (probe: white, #2e2e2e dark) with a fainter border.
        "select:is([multiple],[size]:not([size=\"0\"],[size=\"1\"])):disabled{background-color:light-dark(#fff,#2e2e2e);border-color:light-dark(#e2e2e2,#3b3b3b);}";
}
