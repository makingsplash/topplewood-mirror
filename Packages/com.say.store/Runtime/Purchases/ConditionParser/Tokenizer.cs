using System;
using System.Collections.Generic;
using System.Linq;

namespace SGS.Store.Purchases.ConditionParser
{
    
    //Code based on  https://github.com/hisystems/Interpreter
    internal static class Tokenizer
    {
        private const char _leftParenthesis = '(';
        private const char _rightParenthesis = ')';
        private const char _comma = ',';
        private const char _numericNegative = '-';
        private static readonly char[] _whitespaceCharacters = { ' ', '\t' };
        private static readonly char[]  _numericCharacters = { '.', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' };
        private static readonly char[]  _identifierCharacters = { '_', 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', 'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z' };
        private static readonly char[]  _identifierSecondaryCharacters = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' };     // other characters that can be used as identifiers - but cannot be a starting character
        private static readonly char[]  _textDelimiters = { '\"', '\'' };
        
        /// <summary>
        /// Preliminary tokenization of the expression.
        /// Tokenizes numeric values, alpha values, parentheses, commas and other tokens. 
        /// Any whitespace is removed.
        /// </summary>
        public static List<Token> Parse(string expression)
        {
            List<Token> tokens = new ();
            PeekableEnumerator<char> expressionEnumerator = new PeekableEnumerator<char>(expression);
            
            bool parsingText = false;
            TokenType currentTokenType = TokenType.Other;
            string currentToken = String.Empty;
            char currentTextDelimiter = '\0';
            TokenType characterTokenType = TokenType.Other;

            while (expressionEnumerator.MoveNext())
            {
                var tokenIsSeparateCharacter = false;
                var character = expressionEnumerator.Current;

                // if the character is a '-' and the subsequent character is a numeric character then this is a negative number. 
                // otherwise it is some other character TokenType.Other -- probably a subtraction operator.
                bool isNumericNegative = character == _numericNegative && expressionEnumerator.CanPeek && _numericCharacters.Contains(expressionEnumerator.Peek);

                string characterString;
                if (_textDelimiters.Contains(character) || parsingText)
                {
                    if (_textDelimiters.Contains(character) && !parsingText)         // started parsing
                    {
                        characterTokenType = TokenType.Text;
                        characterString = String.Empty;         // consume character
                        currentTextDelimiter = character;
                        parsingText = true;
                    }
                    else if (character == currentTextDelimiter && parsingText)     // finished parsing
                    {
                        characterString = String.Empty;         // consume character
                        parsingText = false;
                    }
                    else
                    {
                        characterString = character.ToString();
                    }
                }
                else if (_whitespaceCharacters.Contains(character))
                {
                    characterTokenType = TokenType.Whitespace;
                    characterString = String.Empty;             // consume character
                }
                else if (_identifierCharacters.Contains(character) || (currentTokenType == TokenType.Identifier && _identifierSecondaryCharacters.Contains(character)))
                {
                    characterTokenType = TokenType.Identifier;
                    characterString = character.ToString();
                }
                else if (_numericCharacters.Contains(character) || isNumericNegative)
                {
                    characterTokenType = TokenType.Number;
                    characterString = character.ToString();
                }
                else if (character == _leftParenthesis)
                {
                    characterTokenType = TokenType.LeftParenthesis;
                    characterString = character.ToString();
                    tokenIsSeparateCharacter = true;
                }
                else if (character == _rightParenthesis)
                {
                    characterTokenType = TokenType.RightParenthesis;
                    characterString = character.ToString();
                    tokenIsSeparateCharacter = true;
                }
                else if (character == _comma)
                {
                    characterTokenType = TokenType.Comma;
                    characterString = character.ToString();
                    tokenIsSeparateCharacter = true;
                }
                else
                {
                    characterTokenType = TokenType.Other;
                    characterString = character.ToString();
                }

                if (currentTokenType == characterTokenType && !tokenIsSeparateCharacter)
                {
                    currentToken += characterString;
                }
                else
                {
                    if (currentToken.Length > 0 || currentTokenType == TokenType.Text)
                    {
                        tokens.Add(new Token(currentToken, currentTokenType));
                    }

                    currentToken = characterString;
                    currentTokenType = characterTokenType;
                }
            }

            if (currentToken.Length > 0 || currentTokenType == TokenType.Text)
            {
                tokens.Add(new Token(currentToken, currentTokenType));
            }

            return tokens;
        }
    }
}
