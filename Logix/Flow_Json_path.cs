using FrooxEngine;
using FrooxEngine.LogiX;
using Newtonsoft.Json.Linq;
namespace Flow_Json_path
{
    [Category("LogiX/Network")]
    [NodeName("Json path")]
    public class Flow_Json_path : LogixNode
    {
        public readonly Input<string> json;
        public readonly Input<string> jpath;

        public readonly Impulse onParse;
        public readonly Impulse onFail;
        public readonly Output<string> Result;

        [ImpulseTarget]
        public void Parse()
        {
            string input = json.EvaluateRaw(null);
            string argument = jpath.EvaluateRaw(null);
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(argument)) onFail.Trigger();

            JToken token = JObject.Parse(input).SelectToken(argument);
            if (token == null) onFail.Trigger();

            Result.Value = token.ToString();
            onParse.Trigger();
            Result.Value = null;
        }
    }
}
