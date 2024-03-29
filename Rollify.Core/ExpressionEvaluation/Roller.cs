﻿using System;
using System.Collections.Generic;
using System.Text;

using Rollify.Core.Extensions;

namespace Rollify.Core
{

	public class Roller
	{
		public static readonly BigInteger MAX_DIECOUNT = new BigInteger ("1000000", 10); // 10^6
		public static readonly BigInteger MAX_DIETYPE = new BigInteger ("1000000000000000", 10); // 10^15

		abstract class Token
		{
			public abstract void Operate (Stack<BigInteger> stack);
			public override abstract string ToString ();
		}

		class OpToken : Token
		{
			public static readonly OpToken ADD;
			public static readonly OpToken SUB;
			public static readonly OpToken MUL;
			public static readonly OpToken DIV;

			private static Dictionary<string, OpToken> operators;
			public string Symbol { get; private set; }
			public float Precedence { get; private set; }
			private Action<Stack<BigInteger>> implementation;

			static OpToken() {
				ADD = new OpToken ("+", 1.0f, delegate(Stack<BigInteger> stack) { stack.Push (stack.Pop () + stack.Pop ()); });
				SUB = new OpToken ("-", 1.0f, delegate(Stack<BigInteger> stack) {
					BigInteger b = stack.Pop();
					BigInteger a = stack.Pop();
					stack.Push (a - b);
				});
				MUL = new OpToken ("*", 1.0f, delegate(Stack<BigInteger> stack) { stack.Push (stack.Pop () * stack.Pop ()); });
				DIV = new OpToken ("/", 1.0f, delegate(Stack<BigInteger> stack) {
					BigInteger b = stack.Pop();
					if (b == 0)
						throw new InvalidExpressionException("Division by zero");
					BigInteger a = stack.Pop();
					stack.Push (a / b); 
				});
				operators = new Dictionary<string, OpToken>() {
					{ ADD.Symbol, ADD },
					{ SUB.Symbol, SUB },
					{ MUL.Symbol, MUL },
					{ DIV.Symbol, DIV },
				};
			}

			public OpToken(string symbol, float precedence, Action<Stack<BigInteger>> implementation) {
				this.Symbol = symbol;
				this.Precedence = precedence;
				this.implementation = implementation;
			}

			public static OpToken Get(string symbol) {
				return operators.ContainsKey(symbol) ? operators [symbol] : null;
			}

			public override void Operate (Stack<BigInteger> stack) {
				implementation (stack);
			}

			public override string ToString ()
			{
				return Symbol;
			}
		}

		class NumToken : Token
		{
			public BigInteger Num { get; private set; }
			public NumToken(BigInteger num) {
				this.Num = num;
			}

			public override void Operate (Stack<BigInteger> stack) {
				stack.Push (Num);
			}

			public override string ToString ()
			{
				return Num.ToString ();
			}
		}

		class ParenToken : Token
		{
			private Roller r;
			public List<Token> multiplier;
			public List<Token> contents;
			public ParenToken(List<Token> multiplier, List<Token> contents, Roller r) {
				this.multiplier = multiplier;
				this.contents = contents;
				this.r = r;
			}

			public override void Operate(Stack<BigInteger> stack) {
				BigInteger iterations = r.Evaluate (multiplier);

				bool negative = false;
				if (iterations < 0) {
					negative = true;
					iterations = -iterations;
				}
				BigInteger total = 0;
				for (int i = 0; i < iterations; i++) {
					total += r.Evaluate (contents);
				}
				total = negative ? -total : total;
				stack.Push (total);
			}

			public override string ToString ()
			{
				return String.Join (" ", contents);
			}
		}

		class DiceToken : Token
		{
			public Roller r;
			public List<Token> countTokens;
			public BigInteger type;
			public long keepCount;
			public KeepStrategy strategy;

			/// <summary>
			/// Constructs a DiceToken using a postfix token list as the diecount (allowing expressions determining how many dice to roll)
			/// </summary>
			public DiceToken(List<Token> count, BigInteger type, long keepCount, KeepStrategy strategy, Roller r) {
				this.countTokens = count;
				this.type = type;
				this.keepCount = keepCount;
				this.strategy = strategy;
				this.r = r;
			}

			/// <summary>
			/// Constructs a DiceToken using a BigInteger instead of a postfix token list
			/// </summary>
			public DiceToken(BigInteger count, BigInteger type, long keepCount, KeepStrategy strategy, Roller r) : 
				this (new List<Token> (), type, keepCount, strategy, r) {
				this.countTokens.Add (new NumToken(count));
			}

			public override void Operate(Stack<BigInteger> stack) {
				BigInteger count = this.r.Evaluate(countTokens);
				if (count > MAX_DIECOUNT) {
					throw new InvalidExpressionException (count + " is too many dice");
				}
				long countL = count.LongValue ();
				stack.Push(roll(type, countL, keepCount, strategy));
			}

			public override string ToString ()
			{
				string keepS = "";
				if (strategy == KeepStrategy.HIGHEST) {
					keepS = "h" + keepCount;
				} else if (strategy == KeepStrategy.LOWEST) {
					keepS = "l" + keepCount;
				}
				return String.Join (" ", countTokens) + "d" + type.ToString () + keepS;
			}
		}

		private static Random RAND = new Random ();
			

		public string DebugString = "";
		private Database<Formula> formulas;
		private Stack<string> formulaNest; // used to prevent cyclic formula references

		public Roller (Database<Formula> formulas) {
			this.formulas = formulas;
			this.formulaNest = new Stack<string> ();
		}

		public BigInteger Evaluate(string expression) {
			return Evaluate (expression, 0);
		}

		// Evaluates a dice expression
		// Throws InvalidExpressionException with details if the expression is invalid
		private BigInteger Evaluate(string expression, int index, string formulaName = null) {

			List<Token> pfExpression = InfixToPostfix (expression, index, formulaName);
			DebugMessage (expression + " in postfix: " + string.Join (" ", pfExpression));

			return Evaluate (pfExpression);
		}

		/// <summary>
		/// Evaluate an expression that's been converted to postfix
		/// </summary>
		/// <param name="tokens">A list of tokens in postfix notation</param>
		private BigInteger Evaluate(List<Token> postfix) {
			Stack<BigInteger> stack = new Stack<BigInteger> ();
			for (int i = 0; i < postfix.Count; i++) {
				postfix [i].Operate(stack);
			}
			if (stack.Count != 1) {
				throw InvalidExpressionException.DEFAULT;
			}
			return stack.Pop();
		}

		// converts an infix dice formula to a postfix list of tokens. Can return throw InvalidExpressionException if the expression is invalid.
		private List<Token> InfixToPostfix(string input, int index, string formulaName) {

			if (formulaName != null) {
				// we are evaluating a formula, and will push it to the formulaNest so that we can prevent self-referential formulas
				Assert(!formulaNest.Contains (formulaName), "[" + formulaName + "] is self-referential");
				formulaNest.Push(formulaName);
			}

			StringScanner steve = new StringScanner (input, index);
			Stack<string> operatorStack = new Stack<string> ();
			List<Token> output = new List<Token>();
			steve.skipWhitespace ();
			bool lastTokenWasNumber = false;
			while (steve.HasNext()) {
				bool tokenIsNumber = false;
				if (Char.ToLower (steve.Peek ()) == 'd') {
					output.Add (ProcessDieDef (steve, operatorStack, output, lastTokenWasNumber));
					tokenIsNumber = true;
				} else if (IsCharOperator (steve.Peek ())) {
					ProcessOperator (steve, operatorStack, output, lastTokenWasNumber, ref tokenIsNumber);
				} else if (Char.IsDigit (steve.Peek ())) {
					output.Add (new NumToken(steve.ReadLong ()));
					tokenIsNumber = true;
				} else if (steve.Peek () == '(') {
					ProcessParentheses(steve, output, lastTokenWasNumber);
					tokenIsNumber = true;
				} else if (steve.Peek() == '[') {
					ProcessFormula(steve, output, lastTokenWasNumber);
					tokenIsNumber = true;
				} else if (steve.Peek () == ')') {
					// processParentheses reads all the valid close-parens, so if we find one here it must be mismatched
					throw new InvalidExpressionException ("mismatched parentheses");
				} else {
					throw new InvalidExpressionException("invalid symbol: " + steve.Peek());
				}
				steve.skipWhitespace ();
				lastTokenWasNumber = tokenIsNumber;
			}
			while (operatorStack.Count > 0) {
				Assert (operatorStack.Peek () == "(", "mismatched parentheses");
				output.Add(OpToken.Get(operatorStack.Pop ()));
			}

			if (formulaName != null) {
				formulaNest.Pop ();
			}

			return output;
		}

		private Token ProcessDieDef(
				StringScanner steve, 
				Stack<string> opStack, 
				List<Token> output,
				bool lastTokenWasNumber) {
			steve.TrySkip(); // move past the d\
			if (!steve.HasNext())
				throw new InvalidExpressionException("no die type given");
			if (Char.IsDigit (steve.Peek ())) { // check that the syntax is valid before just trying to read it
				Token dieCount = new NumToken(1);
				if (lastTokenWasNumber) {
					// the last number was the die count, because it was followed by a 'd'
					dieCount = output.Last();
					output.RemoveAt (output.Count - 1);
				}
				BigInteger dieType = steve.ReadLong(); // this is safe because we checked that the next char is a digit
				// we now know that die type and the die count, now we need to see if there are extra instructions for the roll
				long keepCount = 1;
				KeepStrategy keepstrat = KeepStrategy.ALL;
				if (steve.HasNext () && char.IsLetter (steve.Peek ()) && Char.ToLower (steve.Peek ()) != 'd') {
					char extension = Char.ToLower (steve.Read ());
					if (extension == 'h') {
						keepstrat = KeepStrategy.HIGHEST;
						BigInteger temp = null;
						if (steve.TryReadLong (ref temp)) {
							if (temp > MAX_DIECOUNT) {
								throw new InvalidExpressionException (temp.ToString () + " is too many dice");
							} else {
								keepCount = temp.LongValue ();
							}
						}
					} else if (extension == 'l') {
						keepstrat = KeepStrategy.LOWEST;
						BigInteger temp = null;
						if (steve.TryReadLong (ref temp)) {
							if (temp > MAX_DIECOUNT) {
								throw new InvalidExpressionException (temp.ToString () + " is too many dice");
							} else {
								keepCount = temp.LongValue ();
							}
						}
					} else {
						throw new InvalidExpressionException("invalid die extension " + extension);
					}
				}
				var countList = new List<Token> ();
				countList.Add (dieCount);
				return new DiceToken (countList, dieType, keepCount, keepstrat, this);
			} else {
				throw new InvalidExpressionException("no die type given");
			}
		}

		// looks to see if the last token can be used as a multiplier. 
		private List<Token> LookForMultiplier(List<Token> tokens, bool lastTokenWasNumber) {
			List<Token> iterationCount = new List<Token>();
			if (lastTokenWasNumber) {
				iterationCount.Add (tokens[tokens.Count - 1]);
				tokens.RemoveAt (tokens.Count - 1);
			} else {
				iterationCount.Add (new NumToken (1));
			}
			return iterationCount;
		}

		// computes and returns the value of an expression in parentheses.
		// The scanner cursor should be pointing at the opening paren when this is called.
		// When this method returns, the scanner cursor will be pointing at the character directly after the closing paren.
		// Returns the expression in parentheses
		private string ExtractParentheses(StringScanner scanner) {
			StringBuilder steve = new StringBuilder ();
			int nestCount = 1; // track nested parentheses
			scanner.TrySkip (); // move past open-paren
			if (!scanner.HasNext ())
				throw new InvalidExpressionException ("mismatched parentheses");
			while (!(scanner.Peek () == ')' && nestCount == 1)) {
				if (scanner.Peek () == '(') {
					nestCount++;
				} else if (scanner.Peek () == ')') {
					nestCount--;
				}
				steve.Append (scanner.Read ());
				if (!scanner.HasNext ())
					throw new InvalidExpressionException ("mismatched parentheses");
			}
			scanner.Read (); // move past close-paren
			DebugMessage ("evaluating \"" + steve.ToString () + "\" in parentheses");
			return steve.ToString ();
		}

		private void ProcessParentheses(StringScanner scanner, List<Token> output, bool lastTokenWasNumber) {
			// If the last token was a number, it's a count of how many times to execute the parenthetic expression.
			// in normal non-random-number math, this is just multiplication, but since we use random numbers,
			// it is implemented as iterative addition, allowing us to re-roll the dice every iteration.
			// If there are no dice defs in the parentheses, then this will yield the same result as normal multiplication.
			string expr = ExtractParentheses (scanner);
			List<Token> multiplier = LookForMultiplier (output, lastTokenWasNumber);
			ParenToken toke = new ParenToken (multiplier, InfixToPostfix (expr, 0, null), this);
			output.Add (toke);
		}

		private void ProcessFormula(StringScanner scanner, List<Token> output, bool lastTokenWasNumber) {
			Formula f = ExtractFormula (scanner);
			List<Token> multiplier = LookForMultiplier (output, lastTokenWasNumber);
			ParenToken toke = new ParenToken (multiplier, InfixToPostfix (f.Expression, 0, f.Name), this);
			output.Add (toke);
		}

		// reads the name of a formula, evaluates its expression, and returns the result.
		// the scanner cursor should be pointing at the opening bracket when this is called.
		// When this method returns, the cursor will be pointing at the character after the closing bracket.
		private Formula ExtractFormula(StringScanner scanner) {
			StringBuilder steve = new StringBuilder ();
			scanner.TrySkip();
			if (!scanner.HasNext ())
				throw new InvalidExpressionException ("mismatched brackets");
			while (scanner.Peek () != ']') {
				steve.Append (scanner.Read ());
				if (!scanner.HasNext ())
					throw new InvalidExpressionException ("mismatched brackets");
			}
			scanner.Read ();
			Formula f = formulas [steve.ToString ()];
			if (f == null)
				throw new InvalidExpressionException ("No formula " + steve.ToString ());
			return f;
		}

		private void ProcessOperator(StringScanner steve, Stack<string> opStack, List<Token> output,
				bool lastTokenWasNumber, ref bool tokenIsNumber) {
			string op = steve.Read ().ToString();
			if (lastTokenWasNumber) {
				PushOperatorToStack (op, opStack, output);
			} else if (op.Equals ("-")) {
				// The last token wasn't a number, but we encountered an operator, so this must be a negative sign
				steve.skipWhitespace ();
				if (!steve.HasNext ()) {
					throw new InvalidExpressionException("misplaced operator: " + op);
				}
				BigInteger num = 1;
				// if there's an expression after the minus sign, just process it and negate it
				if (steve.TryReadLong (ref num) || Char.ToLower (steve.Peek ()) == 'd' || steve.Peek() == '(' || steve.Peek() == '[') {
					output.Add (new NumToken(-num));
					// if the next token is a diedef, paren, or formula, iterativelyAdd will detect the -1 and use it to negate the expression
					tokenIsNumber = true;
				} else { 
					// there is no number after the minus sign, so it can't be negating a number, and it can't be doing subtraction
					throw new InvalidExpressionException ("misplaced operator: " + op);
				}
			} else {
				throw new InvalidExpressionException ("misplaced operator" + op);
			}
		}

		private static void PushOperatorToStack(String op, Stack<string> operatorStack, List<Token> output) {
			float precedence = OperatorPrecedence (op);
			while (operatorStack.Count > 0 && OperatorPrecedence (operatorStack.Peek ()) >= precedence) {
				output.Add (OpToken.Get(operatorStack.Pop ()));
			}
			operatorStack.Push (op);
		}

		private static float OperatorPrecedence(string op) {
			OpToken o = OpToken.Get(op);
			if (o != null) {
				return o.Precedence;
			} else {
				return -1f;
			}
		}

		private static bool IsCharOperator(char c) {
			return c == '+' || c == '-' || c == '*' || c == '/';
		}


		// If we are only keeping some dice, which ones do we keep?
		private enum KeepStrategy {
			ALL, HIGHEST, LOWEST
		}

		// TODO redesign keep strategy to allow keeping both highest and lowest
		private static BigInteger roll(BigInteger dieType, long dieCount, long keepCount, KeepStrategy keepStrategy) {

			if (dieType <= 1) {
				throw new InvalidExpressionException ("invalid die");
			}

			// if the diecount is negative we will roll with the positive diecount, but negate the end result.
			// basically, "-5d6" is treated as "-(5d6)"
			bool negative = false;
			if (dieCount < 0) {
				negative = true;
				dieCount = -dieCount;
			}

			keepCount = Math.Min(keepCount, dieCount);

			// roll the dice and keep them in an array
			BigInteger[] results = new BigInteger[dieCount];
			for (int i = 0; i < dieCount; i++) {
				BigInteger num = new BigInteger ();
				num.genRandomBits (dieType.bitCount (), RAND);
				results [i] = num;
			}

			// add up the results based on the strategy used
			BigInteger result = 0;
			if (keepStrategy == KeepStrategy.ALL) {
				for (int i = 0; i < dieCount; i++) {
					result += results [i];
				}
			} else { // we are only keeping some, so sort the list
				Array.Sort (results);
				if (keepStrategy == KeepStrategy.HIGHEST) {
					for (long i = dieCount - 1; i >= dieCount - keepCount; i--) {
						result += results [i];
					}
				} else if (keepStrategy == KeepStrategy.LOWEST) {
					for (int i = 0; i < keepCount; i++) {
						result += results [i];
					}
				}
			}
			if (negative) {
				result = -result;
			}
			return result;
		}

		private void Assert(bool condition, string errString) {
			if (!condition) {
				throw new InvalidExpressionException (errString);
			}
		}

		private void DebugMessage(string message) {
			DebugString += message + '\n';
		}
	}
}

