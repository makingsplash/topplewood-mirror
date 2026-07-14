using System;

namespace SGS.Store.Purchases.ConditionParser
{
    internal enum TokenType
    {
        /// <summary>
        /// Number
        /// </summary>
        Number,

        /// <summary>
        /// Function, variable name or reserved word
        /// </summary>
        Identifier,

        /// <summary>
        /// '(' character.
        /// </summary>
        LeftParenthesis,

        /// <summary>
        /// ')' character.
        /// </summary>
        RightParenthesis,

        /// <summary>
        /// ',' character.
        /// </summary>
        Comma,

        /// <summary>
        /// String literal, surrounded/delimited by a " character
        /// </summary>
        Text,

        /// <summary>
        /// Only used during parsing - the tokenizer will not return a whitespace token.
        /// </summary>
        Whitespace,

        /// <summary>
        /// Any other token that is not one of the other token types specified in this enum.
        /// Usually a special character such as '*' or '^'.
        /// </summary>
        Other
    }

    /// <summary>
    /// Represents a character or string that is a unique token type.
    /// </summary>
    internal class Token
    {
        private readonly string _value;
        private readonly TokenType _type;

        public Token(string token, TokenType type)
        {
            if (token == null)
            {
                throw new ArgumentNullException();
            }

            _value = token;
            _type = type;
        }

        public string Value
        {
            get
            {
                return _value;
            }
        }

        public TokenType Type
        {
            get
            {
                return _type;
            }
        }

        public override string ToString()
        {
            return _value;
        }

        public override bool Equals(Object value)
        {
            if (value is string)
            {
                return AreEqual(this, (string)value);
            }

            return false;
        }

        public static bool operator ==(Token token1, string token2)
        {
            return AreEqual(token1, token2);
        }

        public static bool operator !=(Token token1, string token2)
        {
            return !AreEqual(token1, token2);
        }

        private static bool AreEqual(Token token1, string token2)
        {
            if (Object.Equals(token1, null) && Object.Equals(token2, null))
            {
                return true;
            }

            if (Object.Equals(token1, null) || Object.Equals(token2, null))
            {
                return false;
            }

            return token1._value.Equals(token2, StringComparison.InvariantCultureIgnoreCase);
        }

        public override int GetHashCode ()
        {
            return base.GetHashCode();
        }
    }
}
