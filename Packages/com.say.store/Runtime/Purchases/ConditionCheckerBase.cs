using System.Collections.Generic;
using System.Linq;
using SGS.Store.Purchases.ConditionParser;
using ConditionFunc = System.Func<SGS.Store.Purchases.IConditionChecker.Context, bool>;

namespace SGS.Store.Purchases
{
    public interface IConditionChecker
    {
        public class Context
        {
            public string ProductId;
            public string ItemId;
            public string BlockId;
        }
        public bool IsConditionMet(Context context, string condition);
    }

    public abstract class ConditionCheckerBase : IConditionChecker
    {
        private readonly Dictionary<string, ConditionFunc> _parsedConditions = new();
        
        public bool IsConditionMet(IConditionChecker.Context context, string condition)
        {
            if (string.IsNullOrEmpty(condition))
            {
                return true;
            }

            if (!_parsedConditions.ContainsKey(condition))
            {
                _parsedConditions[condition] = ParseCondition(condition);
            }
            
            
            return _parsedConditions[condition].Invoke(context);
        }
        protected abstract ConditionFunc CreateCondition(string name, object[] parameters);
        protected abstract void ReportError(string context, string error);

        protected class OrGroup
        {
            public readonly List<AndGroup> Terms = new();
        }

        protected class AndGroup
        {
            public readonly List<NotGroup> Terms = new();
        }

        protected class NotGroup
        {
            public bool Inverse;
            public string Func;
            public readonly List<object> Params = new();
        }

        protected enum State
        {
            Operator,
            NotGroup,
            FuncName,
            Param,
        }

        protected bool DummyCondition(IConditionChecker.Context _)
        {
            return true;
        } 
            
        protected ConditionFunc ParseCondition(string condition)
        {
            OrGroup expression = BuildExpressionTree(condition);

            if (expression == null)
            {
                return DummyCondition;
            }

            List<ConditionFunc> orTerms = new();
            foreach (var andGroup in expression.Terms)
            {
                List<ConditionFunc> andTerms = new();
                foreach (var notGroup in andGroup.Terms)
                {
                    var c = CreateCondition(notGroup.Func, notGroup.Params.ToArray());

                    andTerms.Add(notGroup.Inverse ? p => !c(p) : c);
                }

                orTerms.Add(p => AndOp(andTerms, p));
            }

            return  p => OrOp(orTerms, p);
        }

        private bool AndOp(List<ConditionFunc> terms, IConditionChecker.Context context)
        {
            foreach (var c in terms)
            {
                if (!c(context))
                {
                    return false;
                }
            }

            return true;
        }
        
        private bool OrOp(List<ConditionFunc> terms, IConditionChecker.Context context)
        {
            foreach (var c in terms)
            {
                if (c(context))
                {
                    return true;
                }
            }
            return false;
        }

        protected OrGroup BuildExpressionTree(string condition)
        {
            OrGroup expression = new();
            var tokens = Tokenizer.Parse(condition);
            var tokensEnum = new PeekableEnumerator<Token>(tokens);
            expression.Terms.Add(new AndGroup());

            NotGroup notGroup = null;
            State state = State.NotGroup;

            while (tokensEnum.MoveNext())
            {
                var token = tokensEnum.Current;

                switch (state)
                {
                    case State.Operator:
                        if (token.Type == TokenType.Other && token.Value == "|")
                        {
                            expression.Terms.Add(new AndGroup());
                            state = State.NotGroup;
                        }
                        else if (token.Type == TokenType.Other && token.Value == "&")
                        {
                            state = State.NotGroup;
                        }
                        else
                        {
                            ReportError(GetContext(tokens, token), $"Found token \"{token.Value}\", expecting \"|\" or \"&\"");
                            return null;
                        }
                        break;
                    case State.NotGroup:
                        notGroup = new NotGroup();
                        expression.Terms[^1].Terms.Add(notGroup);
                        if (token.Type == TokenType.Identifier)
                        {
                            notGroup.Func = token.Value;
                            if (tokensEnum.CanPeek && tokensEnum.Peek.Type == TokenType.LeftParenthesis)
                            {
                                tokensEnum.MoveNext();
                                state = State.Param;
                            }
                            else
                            {
                                state = State.Operator;
                                notGroup = null;
                            }
                        }
                        else if (token.Type == TokenType.Other && token.Value == "!")
                        {
                            notGroup.Inverse = true;
                            state = State.FuncName;
                        }
                        else
                        {
                            ReportError(GetContext(tokens, token), $"Found token \"{token.Value}\", expecting function name or \"!\"");
                            return null;
                        }

                        break;
                    case State.FuncName:
                        if (token.Type == TokenType.Identifier)
                        {
                            notGroup.Func = token.Value;
                            if (tokensEnum.CanPeek && tokensEnum.Peek.Type == TokenType.LeftParenthesis)
                            {
                                tokensEnum.MoveNext();
                                state = State.Param;
                            }
                            else
                            {
                                state = State.Operator;
                                notGroup = null;
                            }
                        }
                        else
                        {
                            ReportError(GetContext(tokens, token), $"Found token \"{token.Value}\", expecting function name");
                            return null;
                        }

                        break;
                    case State.Param:
                        if (token.Type == TokenType.Number || token.Type == TokenType.Text)
                        {
                            AddParameter(token, notGroup);
                        }
                        else if (token.Type == TokenType.Comma)
                        {
                        }
                        else if (token.Type == TokenType.RightParenthesis)
                        {
                            state = State.Operator;
                            notGroup = null;
                        }
                        else
                        {
                            ReportError(GetContext(tokens, token), $"Found token \"{token.Value}\", expecting literal, comma or \")\"");
                            return null;
                        }
                        break;
                }
            }

            return expression;
        }

        private static void AddParameter(Token token, NotGroup notGroup)
        {
            if (token.Type == TokenType.Number)
            {
                if (token.Value.Contains('.'))
                {
                    notGroup.Params.Add(float.Parse(token.Value));
                }
                else
                {
                    notGroup.Params.Add(int.Parse(token.Value));
                }
            }
            else
            {
                notGroup.Params.Add(token.Value);
            }
        }

        private string GetContext(List<Token> tokens, Token token)
        {
            return string.Join("", tokens.TakeWhile(t => !ReferenceEquals(t, token)));
        }
   }

}