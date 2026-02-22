using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Nori.Compiler
{
    public class IrLowering
    {
        private readonly ModuleDecl _module;
        private readonly DiagnosticBag _diagnostics;
        private readonly IrModule _ir = new IrModule();

        private int _tempCounter;
        private int _labelCounter;
        private IrBlock _currentBlock;

        // Constant deduplication: (udonType, valueString) -> varName
        private readonly Dictionary<(string, string), string> _constants
            = new Dictionary<(string, string), string>();

        // Break/continue targets for loops
        private readonly Stack<(string breakLabel, string continueLabel)> _loopStack
            = new Stack<(string, string)>();

        // Current function name (null if in event handler or top-level)
        private string _currentFunctionName;

        // Current function return type (resolved Udon type, null if void or not in function)
        private string _currentFunctionReturnType;

        // Extern catalog for implicit conversions
        private readonly IExternCatalog _catalog;

        // This-object references
        private string _thisUdonBehaviour;
        private string _thisGameObject;
        private string _thisTransform;

        // Top-level boolean vars that need runtime init to true
        // (Udon data section can't represent boolean True, only null which = false)
        private readonly List<string> _deferredTrueInits = new List<string>();

        // Top-level vars with non-literal initializers that need runtime init in _start
        private readonly List<(string varName, Expr initializer)> _deferredExprInits
            = new List<(string, Expr)>();

        // Maps source-level variable names to their current heap variable names.
        // Handles cases where DeclareHeapVar renames a variable to avoid collisions.
        private readonly Dictionary<string, string> _varNameMap
            = new Dictionary<string, string>();

        // Functions referenced in send statements that need exported wrappers
        private readonly HashSet<string> _networkSentFunctions = new HashSet<string>();

        // All declared function names (to distinguish functions from custom events in send)
        private readonly HashSet<string> _declaredFunctions = new HashSet<string>();

        public IrLowering(ModuleDecl module, DiagnosticBag diagnostics,
            IExternCatalog catalog = null)
        {
            _module = module;
            _diagnostics = diagnostics;
            _catalog = catalog ?? BuiltinCatalog.Instance;
        }

        public IrModule Lower()
        {
            // Create "this" references
            _thisUdonBehaviour = DeclareHeapVar("__this_VRCUdonUdonBehaviour_0",
                "VRCUdonUdonBehaviour", "this");
            _ir.FindVar(_thisUdonBehaviour).IsThis = true;

            _thisGameObject = DeclareHeapVar("__this_UnityEngineGameObject_0",
                "UnityEngineGameObject", "this");
            _ir.FindVar(_thisGameObject).IsThis = true;

            _thisTransform = DeclareHeapVar("__this_UnityEngineTransform_0",
                "UnityEngineTransform", "this");
            _ir.FindVar(_thisTransform).IsThis = true;

            // Lower all declarations
            foreach (var decl in _module.Declarations)
            {
                switch (decl)
                {
                    case VarDecl v:
                        LowerVarDecl(v);
                        break;
                    case EventHandlerDecl e:
                        LowerEventHandler(e);
                        break;
                    case CustomEventDecl ce:
                        LowerCustomEvent(ce);
                        break;
                    case FunctionDecl f:
                        LowerFunction(f);
                        break;
                }
            }

            // Generate exported wrappers for functions referenced in network send statements.
            // VRC's SendCustomNetworkEvent("DoWork") expects an exported "DoWork:" label,
            // but functions are lowered as "__fn_DoWork:" (non-exported). The wrapper calls
            // the function using the same JUMP_INDIRECT mechanism as LowerFunctionCall.
            foreach (var funcName in _networkSentFunctions)
            {
                var wrapperBlock = new IrBlock(funcName, true);
                _ir.Blocks.Add(wrapperBlock);
                _currentBlock = wrapperBlock;

                string retAddrVar = $"__retaddr_{funcName}";
                string returnLabel = $"__send_ret_{funcName}";
                string retAddrConst = GetOrCreateConstant("SystemUInt32", $"__label__{returnLabel}");
                Emit(new IrCopy(retAddrConst, retAddrVar));
                Emit(new IrJump($"__fn_{funcName}"));

                var retBlock = new IrBlock(returnLabel);
                _ir.Blocks.Add(retBlock);
                _currentBlock = retBlock;
                Emit(IrJump.Halt());
            }

            // Inject runtime init for top-level booleans defaulting to true.
            // Udon data section can only hold null (=false), so we compute true
            // via negation at the start of _start.
            if (_deferredTrueInits.Count > 0)
            {
                var startBlock = _ir.Blocks.FirstOrDefault(b => b.Label == "_start");
                if (startBlock == null)
                {
                    startBlock = new IrBlock("_start", true);
                    _ir.Blocks.Insert(0, startBlock);
                    startBlock.Instructions.Add(IrJump.Halt());
                }

                var savedBlock = _currentBlock;
                _currentBlock = startBlock;

                string falseConst = GetOrCreateConstant("SystemBoolean", "null");
                var initInstructions = new List<IrInstruction>();
                foreach (var varName in _deferredTrueInits)
                {
                    string trueTemp = AllocTemp("SystemBoolean");
                    initInstructions.Add(new IrPush(falseConst));
                    initInstructions.Add(new IrPush(trueTemp));
                    initInstructions.Add(new IrExtern("SystemBoolean.__op_UnaryNegation__SystemBoolean__SystemBoolean"));
                    initInstructions.Add(new IrCopy(trueTemp, varName));
                }
                startBlock.Instructions.InsertRange(0, initInstructions);

                _currentBlock = savedBlock;
            }

            // Inject runtime init for top-level vars with non-literal initializers
            // (e.g., enum constants, expressions). These get evaluated at the start of _start.
            if (_deferredExprInits.Count > 0)
            {
                var startBlock = _ir.Blocks.FirstOrDefault(b => b.Label == "_start");
                if (startBlock == null)
                {
                    startBlock = new IrBlock("_start", true);
                    _ir.Blocks.Insert(0, startBlock);
                    startBlock.Instructions.Add(IrJump.Halt());
                }

                var savedBlock = _currentBlock;
                _currentBlock = startBlock;

                // LowerExpr uses Emit() which appends to _currentBlock.
                // Track where new instructions start so we can move them to the front.
                int before = startBlock.Instructions.Count;

                foreach (var (varName, expr) in _deferredExprInits)
                {
                    string valVar = LowerExpr(expr);
                    Emit(new IrCopy(valVar, varName));
                }

                // Move newly emitted instructions to the front of _start
                // (after any _deferredTrueInits code that was already inserted at 0)
                int newCount = startBlock.Instructions.Count - before;
                if (newCount > 0 && before > 0)
                {
                    var newInstrs = startBlock.Instructions.GetRange(before, newCount);
                    startBlock.Instructions.RemoveRange(before, newCount);
                    startBlock.Instructions.InsertRange(0, newInstrs);
                }

                _currentBlock = savedBlock;
            }

            return _ir;
        }

        // --- Variable declarations ---

        private void LowerVarDecl(VarDecl v)
        {
            string udonType = TypeSystem.ResolveType(v.IsArray ? v.TypeName + "[]" : v.TypeName)
                ?? "SystemObject";
            string initVal = GetInitialValue(v.Initializer, udonType);

            var heapVar = new IrHeapVar(v.Name, udonType, initVal)
            {
                IsExport = v.IsPublic,
                SyncMode = v.SyncMode,
            };
            _ir.HeapVars.Add(heapVar);

            // Track booleans with true initializers — they need runtime init
            // because the data section can only represent null (= false)
            if (v.Initializer is BoolLiteralExpr boolLit && boolLit.Value)
            {
                _deferredTrueInits.Add(v.Name);
            }
            // Track non-literal initializers for deferred runtime init in _start
            else if (v.Initializer != null && !IsSimpleLiteral(v.Initializer))
            {
                _deferredExprInits.Add((v.Name, v.Initializer));
            }
        }

        private static bool IsSimpleLiteral(Expr expr)
        {
            return expr is IntLiteralExpr || expr is FloatLiteralExpr ||
                   expr is BoolLiteralExpr || expr is StringLiteralExpr ||
                   expr is NullLiteralExpr;
        }

        // --- Event handlers ---

        // VRC's runtime uses fixed internal parameter names for input events,
        // not the user-written parameter names. Button events use "boolValue",
        // axis events use "floatValue". The mangled heap var name is constructed
        // from these (e.g., _inputJump + boolValue → inputJumpBoolValue).
        private static readonly Dictionary<string, string[]> InputEventRuntimeParams
            = new Dictionary<string, string[]>
        {
            ["_inputJump"] = new[] { "boolValue" },
            ["_inputUse"] = new[] { "boolValue" },
            ["_inputGrab"] = new[] { "boolValue" },
            ["_inputDrop"] = new[] { "boolValue" },
            ["_inputMoveHorizontal"] = new[] { "floatValue" },
            ["_inputMoveVertical"] = new[] { "floatValue" },
            ["_inputLookHorizontal"] = new[] { "floatValue" },
            ["_inputLookVertical"] = new[] { "floatValue" },
            ["_onPlayerTriggerEnter"] = new[] { "player" },
            ["_onPlayerTriggerExit"] = new[] { "player" },
            ["_onPlayerCollisionEnter"] = new[] { "player" },
            ["_onPlayerCollisionExit"] = new[] { "player" },
            ["_onPlayerParticleCollision"] = new[] { "player" },
            ["_onPlayerJoined"] = new[] { "player" },
            ["_onPlayerLeft"] = new[] { "player" },
        };

        // Implicit event parameters that VRC passes via mangled heap vars
        private static readonly Dictionary<string, (string paramName, string paramType)> ImplicitEventParams
            = new Dictionary<string, (string, string)>
        {
            ["_onStringLoadSuccess"] = ("result", "VRCSDK3StringLoadingIVRCStringDownload"),
            ["_onStringLoadError"] = ("result", "VRCSDK3StringLoadingIVRCStringDownload"),
            ["_onImageLoadSuccess"] = ("result", "VRCSDK3ImageIVRCImageDownload"),
            ["_onImageLoadError"] = ("result", "VRCSDK3ImageIVRCImageDownload"),
        };

        private void LowerEventHandler(EventHandlerDecl handler)
        {
            string label = SemanticAnalyzer.GetUdonEventName(handler.EventName);
            var block = new IrBlock(label, true);
            _ir.Blocks.Add(block);
            _currentBlock = block;

            // Declare parameter variables (event parameters are heap vars)
            // Udon runtime mangles event param names: _onPlayerJoined + player → onPlayerJoinedPlayer
            // We must declare the mangled name (runtime writes to it) and copy to the original name.
            // For input events, VRC uses fixed internal names (boolValue/floatValue) regardless
            // of what the user names the parameter in source.
            InputEventRuntimeParams.TryGetValue(label, out var runtimeParamNames);

            for (int i = 0; i < handler.Parameters.Count; i++)
            {
                var param = handler.Parameters[i];
                string paramType = TypeSystem.ResolveType(param.TypeName) ?? "SystemObject";

                // Use VRC's runtime parameter name if this is an input event
                string runtimeName = (runtimeParamNames != null && i < runtimeParamNames.Length)
                    ? runtimeParamNames[i]
                    : param.Name;
                string mangledName = $"{label.Substring(1)}{char.ToUpper(runtimeName[0])}{runtimeName.Substring(1)}";

                DeclareHeapVar(mangledName, paramType);
                string localVar = DeclareHeapVar(param.Name, paramType);
                _varNameMap[param.Name] = localVar;
                Emit(new IrCopy(mangledName, localVar));
            }

            // Handle implicit event parameters (e.g., 'result' for download events)
            if (ImplicitEventParams.TryGetValue(label, out var implicitParam))
            {
                string paramType = implicitParam.paramType;
                string mangledName = $"{label.Substring(1)}{char.ToUpper(implicitParam.paramName[0])}{implicitParam.paramName.Substring(1)}";
                DeclareHeapVar(mangledName, paramType);
                string localVar = DeclareHeapVar(implicitParam.paramName, paramType);
                _varNameMap[implicitParam.paramName] = localVar;
                Emit(new IrCopy(mangledName, localVar));
            }

            LowerBlock(handler.Body);

            // Events end with halt sentinel
            Emit(IrJump.Halt());
        }

        // --- Custom events ---

        private void LowerCustomEvent(CustomEventDecl ce)
        {
            var block = new IrBlock(ce.Name, true);
            _ir.Blocks.Add(block);
            _currentBlock = block;

            LowerBlock(ce.Body);

            Emit(IrJump.Halt());
        }

        // --- Functions ---

        private void LowerFunction(FunctionDecl func)
        {
            _declaredFunctions.Add(func.Name);

            // Declare function infrastructure vars
            string retAddrVar = DeclareHeapVar($"__retaddr_{func.Name}", "SystemUInt32", "0");

            foreach (var param in func.Parameters)
            {
                string paramType = TypeSystem.ResolveType(param.TypeName) ?? "SystemObject";
                DeclareHeapVar($"__param_{func.Name}_{param.Name}", paramType);
            }

            string retValVar = null;
            if (func.ReturnTypeName != null)
            {
                string retType = TypeSystem.ResolveType(func.ReturnTypeName) ?? "SystemObject";
                retValVar = DeclareHeapVar($"__retval_{func.Name}", retType);
            }

            // Create the function block
            var block = new IrBlock($"__fn_{func.Name}");
            _ir.Blocks.Add(block);
            _currentBlock = block;

            var prevFunctionName = _currentFunctionName;
            _currentFunctionName = func.Name;

            var prevReturnType = _currentFunctionReturnType;
            _currentFunctionReturnType = func.ReturnTypeName != null
                ? TypeSystem.ResolveType(func.ReturnTypeName) : null;

            // Copy param slots to local names
            var prevParamMappings = new Dictionary<string, string>();
            foreach (var param in func.Parameters)
            {
                string paramType = TypeSystem.ResolveType(param.TypeName) ?? "SystemObject";
                string localVar = DeclareHeapVar(param.Name, paramType);
                Emit(new IrCopy($"__param_{func.Name}_{param.Name}", localVar));

                _varNameMap.TryGetValue(param.Name, out var prev);
                prevParamMappings[param.Name] = prev;
                _varNameMap[param.Name] = localVar;
            }

            LowerBlock(func.Body);

            // Restore parameter mappings
            foreach (var kvp in prevParamMappings)
            {
                if (kvp.Value != null) _varNameMap[kvp.Key] = kvp.Value;
                else _varNameMap.Remove(kvp.Key);
            }

            // End with indirect jump to return address
            Emit(new IrJumpIndirect(retAddrVar));

            _currentFunctionName = prevFunctionName;
            _currentFunctionReturnType = prevReturnType;
        }

        // --- Statement lowering ---

        private void LowerBlock(List<Stmt> stmts)
        {
            foreach (var stmt in stmts)
                LowerStmt(stmt);
        }

        private void LowerStmt(Stmt stmt)
        {
            switch (stmt)
            {
                case LocalVarStmt lv:
                    LowerLocalVar(lv);
                    break;
                case AssignStmt assign:
                    LowerAssign(assign);
                    break;
                case IfStmt ifStmt:
                    LowerIf(ifStmt);
                    break;
                case WhileStmt whileStmt:
                    LowerWhile(whileStmt);
                    break;
                case ForRangeStmt forRange:
                    LowerForRange(forRange);
                    break;
                case ForEachStmt forEach:
                    LowerForEach(forEach);
                    break;
                case ReturnStmt ret:
                    LowerReturn(ret);
                    break;
                case BreakStmt _:
                    if (_loopStack.Count > 0)
                        Emit(new IrJump(_loopStack.Peek().breakLabel));
                    break;
                case ContinueStmt _:
                    if (_loopStack.Count > 0)
                        Emit(new IrJump(_loopStack.Peek().continueLabel));
                    break;
                case SendStmt send:
                    LowerSend(send);
                    break;
                case ExpressionStmt exprStmt:
                    LowerExpr(exprStmt.Expression);
                    break;
            }
        }

        private void LowerLocalVar(LocalVarStmt lv)
        {
            string udonType = TypeSystem.ResolveType(lv.IsArray ? lv.TypeName + "[]" : lv.TypeName)
                ?? "SystemObject";
            string initVal = GetInitialValue(lv.Initializer, udonType);
            string varName = DeclareHeapVar(lv.Name, udonType, initVal);
            _varNameMap[lv.Name] = varName;

            if (lv.Initializer != null)
            {
                string valVar = LowerExpr(lv.Initializer);

                // Emit implicit conversion if expression type differs from variable type
                string exprType = lv.Initializer.ResolvedType;
                if (exprType != null && exprType != udonType)
                {
                    var conversion = _catalog.GetImplicitConversion(exprType, udonType);
                    if (conversion != null)
                        valVar = EmitConversion(valVar, conversion);
                }

                Emit(new IrCopy(valVar, varName));
            }
        }

        private void LowerAssign(AssignStmt assign)
        {
            string valueVar = LowerExpr(assign.Value);

            // Handle compound assignment
            if (assign.Op != AssignOp.Assign)
            {
                string leftType = assign.Target.ResolvedType ?? "SystemObject";

                // For compound assignment to properties (MemberExpr), emit getter first
                string targetVar;
                if (assign.Target is MemberExpr compoundMember && compoundMember.ResolvedGetter != null)
                {
                    targetVar = LowerMemberAccess(compoundMember);
                }
                else
                {
                    targetVar = LowerExpr(assign.Target);
                }

                // Use the resolved operator from semantic analysis
                string opExtern = assign.ResolvedOperator?.Extern;
                if (opExtern != null)
                {
                    string resultVar = AllocTemp(assign.ResolvedOperator.ReturnType ?? leftType);
                    Emit(new IrPush(targetVar));
                    Emit(new IrPush(valueVar));
                    Emit(new IrPush(resultVar));
                    Emit(new IrExtern(opExtern));
                    valueVar = resultVar;
                }
            }

            // Handle different assignment targets
            if (assign.Target is NameExpr nameTarget)
            {
                string targetName = ResolveVarName(nameTarget);

                // Emit implicit conversion if value type differs from target type
                string targetType = nameTarget.ResolvedType;
                string valueType = assign.Value.ResolvedType;
                if (targetType != null && valueType != null && valueType != targetType)
                {
                    var conversion = _catalog.GetImplicitConversion(valueType, targetType);
                    if (conversion != null)
                        valueVar = EmitConversion(valueVar, conversion);
                }

                Emit(new IrCopy(valueVar, targetName));
            }
            else if (assign.Target is MemberExpr memberTarget)
            {
                LowerMemberAssign(memberTarget, valueVar);
            }
            else if (assign.Target is IndexExpr indexTarget)
            {
                LowerIndexAssign(indexTarget, valueVar);
            }
        }

        private void LowerMemberAssign(MemberExpr member, string valueVar)
        {
            if (member.ResolvedSetter != null)
            {
                string objVar = LowerExpr(member.Object);

                if (member.ResolvedSetter.IsInstance)
                {
                    Emit(new IrPush(objVar));
                }
                Emit(new IrPush(valueVar));
                Emit(new IrExtern(member.ResolvedSetter.Extern));
            }
        }

        private void LowerIndexAssign(IndexExpr index, string valueVar)
        {
            string arrayVar = LowerExpr(index.Object);
            string indexVar = LowerExpr(index.Index);

            string arrayType = index.Object.ResolvedType ?? "SystemObjectArray";
            string elemType = arrayType.EndsWith("Array")
                ? arrayType.Substring(0, arrayType.Length - 5) : "SystemObject";

            string setExtern = $"{arrayType}.__Set__SystemInt32_{elemType}__SystemVoid";

            Emit(new IrPush(arrayVar));
            Emit(new IrPush(indexVar));
            Emit(new IrPush(valueVar));
            Emit(new IrExtern(setExtern));
        }

        private void LowerIf(IfStmt ifStmt)
        {
            string condVar = LowerExpr(ifStmt.Condition);
            string elseLabel = NewLabel("else");
            string endLabel = NewLabel("endif");

            Emit(new IrJumpIfFalse(condVar, ifStmt.ElseBody != null ? elseLabel : endLabel));

            LowerBlock(ifStmt.ThenBody);
            if (ifStmt.ElseBody != null)
            {
                Emit(new IrJump(endLabel));
                StartContinuationBlock(elseLabel);
                LowerBlock(ifStmt.ElseBody);
            }

            StartContinuationBlock(endLabel);
        }

        private void LowerWhile(WhileStmt whileStmt)
        {
            string condLabel = NewLabel("while_cond");
            string bodyLabel = NewLabel("while_body");
            string endLabel = NewLabel("while_end");

            Emit(new IrJump(condLabel));
            StartContinuationBlock(condLabel);

            string condVar = LowerExpr(whileStmt.Condition);
            Emit(new IrJumpIfFalse(condVar, endLabel));

            _loopStack.Push((endLabel, condLabel));
            LowerBlock(whileStmt.Body);
            _loopStack.Pop();

            Emit(new IrJump(condLabel));
            StartContinuationBlock(endLabel);
        }

        private void LowerForRange(ForRangeStmt forRange)
        {
            string iterVar = DeclareHeapVar(forRange.VarName, "SystemInt32");

            // Save previous mapping and register loop variable
            _varNameMap.TryGetValue(forRange.VarName, out var prevForMapping);
            _varNameMap[forRange.VarName] = iterVar;

            string startVar = LowerExpr(forRange.Start);
            Emit(new IrCopy(startVar, iterVar));

            string limitVar = LowerExpr(forRange.End);
            string condLabel = NewLabel("for_cond");
            string incrLabel = NewLabel("for_incr");
            string endLabel = NewLabel("for_end");

            Emit(new IrJump(condLabel));
            StartContinuationBlock(condLabel);

            // Compare iter < limit
            string condResult = AllocTemp("SystemBoolean");
            Emit(new IrPush(iterVar));
            Emit(new IrPush(limitVar));
            Emit(new IrPush(condResult));
            Emit(new IrExtern("SystemInt32.__op_LessThan__SystemInt32_SystemInt32__SystemBoolean"));
            Emit(new IrJumpIfFalse(condResult, endLabel));

            _loopStack.Push((endLabel, incrLabel));
            LowerBlock(forRange.Body);
            _loopStack.Pop();

            // Increment
            StartContinuationBlock(incrLabel);
            string oneConst = GetOrCreateConstant("SystemInt32", "1");
            string incrResult = AllocTemp("SystemInt32");
            Emit(new IrPush(iterVar));
            Emit(new IrPush(oneConst));
            Emit(new IrPush(incrResult));
            Emit(new IrExtern("SystemInt32.__op_Addition__SystemInt32_SystemInt32__SystemInt32"));
            Emit(new IrCopy(incrResult, iterVar));

            Emit(new IrJump(condLabel));
            StartContinuationBlock(endLabel);

            // Restore previous mapping
            if (prevForMapping != null)
                _varNameMap[forRange.VarName] = prevForMapping;
            else
                _varNameMap.Remove(forRange.VarName);
        }

        private void LowerForEach(ForEachStmt forEach)
        {
            string arrayVar = LowerExpr(forEach.Collection);
            string arrayType = forEach.Collection.ResolvedType ?? "SystemObjectArray";
            string elemType = arrayType.EndsWith("Array")
                ? arrayType.Substring(0, arrayType.Length - 5) : "SystemObject";

            // Get length
            string lengthVar = AllocTemp("SystemInt32");
            Emit(new IrPush(arrayVar));
            Emit(new IrPush(lengthVar));
            Emit(new IrExtern($"{arrayType}.__get_Length__SystemInt32"));

            // Init index
            string indexVar = AllocTemp("SystemInt32");
            string zeroConst = GetOrCreateConstant("SystemInt32", "0");
            Emit(new IrCopy(zeroConst, indexVar));

            string condLabel = NewLabel("foreach_cond");
            string incrLabel = NewLabel("foreach_incr");
            string endLabel = NewLabel("foreach_end");
            string elemVar = DeclareHeapVar(forEach.VarName, elemType);

            // Save previous mapping and register loop variable
            _varNameMap.TryGetValue(forEach.VarName, out var prevForEachMapping);
            _varNameMap[forEach.VarName] = elemVar;

            Emit(new IrJump(condLabel));
            StartContinuationBlock(condLabel);

            // Compare index < length
            string condResult = AllocTemp("SystemBoolean");
            Emit(new IrPush(indexVar));
            Emit(new IrPush(lengthVar));
            Emit(new IrPush(condResult));
            Emit(new IrExtern("SystemInt32.__op_LessThan__SystemInt32_SystemInt32__SystemBoolean"));
            Emit(new IrJumpIfFalse(condResult, endLabel));

            // Get element
            string getResult = AllocTemp(elemType);
            Emit(new IrPush(arrayVar));
            Emit(new IrPush(indexVar));
            Emit(new IrPush(getResult));
            Emit(new IrExtern($"{arrayType}.__Get__SystemInt32__{elemType}"));
            Emit(new IrCopy(getResult, elemVar));

            _loopStack.Push((endLabel, incrLabel));
            LowerBlock(forEach.Body);
            _loopStack.Pop();

            // Increment index
            StartContinuationBlock(incrLabel);
            string oneConst = GetOrCreateConstant("SystemInt32", "1");
            string incrResult = AllocTemp("SystemInt32");
            Emit(new IrPush(indexVar));
            Emit(new IrPush(oneConst));
            Emit(new IrPush(incrResult));
            Emit(new IrExtern("SystemInt32.__op_Addition__SystemInt32_SystemInt32__SystemInt32"));
            Emit(new IrCopy(incrResult, indexVar));

            Emit(new IrJump(condLabel));
            StartContinuationBlock(endLabel);

            // Restore previous mapping
            if (prevForEachMapping != null)
                _varNameMap[forEach.VarName] = prevForEachMapping;
            else
                _varNameMap.Remove(forEach.VarName);
        }

        private void LowerReturn(ReturnStmt ret)
        {
            if (ret.Value != null && _currentFunctionName != null)
            {
                string valVar = LowerExpr(ret.Value);

                // Emit implicit conversion if return expression type differs from function return type
                string exprType = ret.Value.ResolvedType;
                if (_currentFunctionReturnType != null && exprType != null
                    && exprType != _currentFunctionReturnType)
                {
                    var conversion = _catalog.GetImplicitConversion(exprType, _currentFunctionReturnType);
                    if (conversion != null)
                        valVar = EmitConversion(valVar, conversion);
                }

                string retValVar = $"__retval_{_currentFunctionName}";
                Emit(new IrCopy(valVar, retValVar));
            }

            if (_currentFunctionName != null)
            {
                Emit(new IrJumpIndirect($"__retaddr_{_currentFunctionName}"));
            }
            else
            {
                // Return in event handler - same as halt
                Emit(IrJump.Halt());
            }
        }

        private void LowerSend(SendStmt send)
        {
            if (send.Target == null)
            {
                // Local send: SendCustomEvent
                string eventNameConst = GetOrCreateConstant("SystemString", $"\"{send.EventName}\"");
                Emit(new IrPush(_thisUdonBehaviour));
                Emit(new IrPush(eventNameConst));
                Emit(new IrExtern("VRCUdonCommonInterfacesIUdonEventReceiver.__SendCustomEvent__SystemString__SystemVoid"));
            }
            else
            {
                // Track functions referenced via network send (they need exported wrappers)
                if (_declaredFunctions.Contains(send.EventName))
                    _networkSentFunctions.Add(send.EventName);
                // Network send: SendCustomNetworkEvent
                // Use SystemInt32 for the target enum value — VRC's text assembler can't
                // resolve the enum type for data declarations, but the extern handles
                // int→enum conversion at runtime.
                string targetConst;
                if (send.Target == "All")
                    targetConst = GetOrCreateConstant("SystemInt32", "1");
                else // Owner
                    targetConst = GetOrCreateConstant("SystemInt32", "0");

                string eventNameConst = GetOrCreateConstant("SystemString", $"\"{send.EventName}\"");

                Emit(new IrPush(_thisUdonBehaviour));
                Emit(new IrPush(targetConst));
                Emit(new IrPush(eventNameConst));
                Emit(new IrExtern("VRCUdonCommonInterfacesIUdonEventReceiver.__SendCustomNetworkEvent__VRCSDKBaseNetworkingNetworkEventTarget_SystemString__SystemVoid"));
            }
        }

        // --- Expression lowering ---
        // Returns the name of the heap var holding the result

        private string LowerExpr(Expr expr)
        {
            switch (expr)
            {
                case IntLiteralExpr intLit:
                    return GetOrCreateConstant("SystemInt32", intLit.Value.ToString());

                case FloatLiteralExpr floatLit:
                    return GetOrCreateConstant("SystemSingle",
                        floatLit.Value.ToString(CultureInfo.InvariantCulture));

                case BoolLiteralExpr boolLit:
                    // Boolean heap vars always initialize to null (= false) in Udon.
                    // For 'false', just return the null-initialized constant.
                    // For 'true', negate the false constant at runtime.
                    string falseConst = GetOrCreateConstant("SystemBoolean", "null");
                    if (!boolLit.Value)
                        return falseConst;
                    string trueVar = AllocTemp("SystemBoolean");
                    Emit(new IrPush(falseConst));
                    Emit(new IrPush(trueVar));
                    Emit(new IrExtern("SystemBoolean.__op_UnaryNegation__SystemBoolean__SystemBoolean"));
                    return trueVar;

                case StringLiteralExpr strLit:
                    return GetOrCreateConstant("SystemString", $"\"{EscapeString(strLit.Value)}\"");

                case NullLiteralExpr _:
                    return GetOrCreateConstant("UnityEngineObject", "null");

                case InterpolatedStringExpr interp:
                    return LowerInterpolation(interp);

                case NameExpr name:
                    return ResolveVarName(name);

                case BinaryExpr binary:
                    return LowerBinary(binary);

                case UnaryExpr unary:
                    return LowerUnary(unary);

                case MemberExpr member:
                    return LowerMemberAccess(member);

                case CallExpr call:
                    return LowerCall(call);

                case IndexExpr index:
                    return LowerIndex(index);

                case ArrayLiteralExpr arr:
                    return LowerArrayLiteral(arr);

                case CastExpr cast:
                    return LowerCast(cast);

                default:
                    return AllocTemp("SystemObject");
            }
        }

        private string LowerInterpolation(InterpolatedStringExpr interp)
        {
            if (interp.Parts.Count == 0)
                return GetOrCreateConstant("SystemString", "\"\"");

            // Convert each part to string and concatenate
            string result = null;
            foreach (var part in interp.Parts)
            {
                string partVar = LowerExpr(part);

                // Convert to string if not already
                if (part.ResolvedType != "SystemString" && !(part is StringLiteralExpr))
                {
                    string strVar = AllocTemp("SystemString");
                    Emit(new IrPush(partVar));
                    Emit(new IrPush(strVar));
                    Emit(new IrExtern("SystemObject.__ToString__SystemString"));
                    partVar = strVar;
                }

                if (result == null)
                {
                    result = partVar;
                }
                else
                {
                    // Concat
                    string concatResult = AllocTemp("SystemString");
                    Emit(new IrPush(result));
                    Emit(new IrPush(partVar));
                    Emit(new IrPush(concatResult));
                    Emit(new IrExtern("SystemString.__Concat__SystemString_SystemString__SystemString"));
                    result = concatResult;
                }
            }

            return result ?? GetOrCreateConstant("SystemString", "\"\"");
        }

        private string ResolveVarName(NameExpr name)
        {
            if (name.ResolvedSymbol == null)
                return AllocTemp("SystemObject");

            switch (name.ResolvedSymbol.Kind)
            {
                case SymbolKind.Builtin:
                    switch (name.Name)
                    {
                        case "gameObject": return _thisGameObject;
                        case "transform": return _thisTransform;
                        case "localPlayer":
                        {
                            // Emit Networking.get_LocalPlayer
                            string result = AllocTemp("VRCSDKBaseVRCPlayerApi");
                            Emit(new IrPush(result));
                            Emit(new IrExtern("VRCSDKBaseNetworking.__get_LocalPlayer__VRCSDKBaseVRCPlayerApi"));
                            return result;
                        }
                    }
                    break;

                case SymbolKind.StaticType:
                case SymbolKind.EnumType:
                    // If this NameExpr is used as a call argument (type-as-value),
                    // emit as SystemString with the CLR type name. Udon's assembler
                    // can't initialize SystemType from a string in the data section,
                    // but the string-based GetComponent overload works correctly.
                    if (name.ResolvedType == "SystemType")
                    {
                        string udonType = name.ResolvedSymbol.UdonType;
                        string clrName = _catalog.GetClrTypeName(udonType);
                        return GetOrCreateConstant("SystemString", $"\"{clrName}\"");
                    }
                    return AllocTemp(name.ResolvedSymbol.UdonType);
            }

            // Regular variable - check name map for remapped heap var names
            if (_varNameMap.TryGetValue(name.Name, out var mappedName))
                return mappedName;

            // Ensure it exists as a heap var
            if (_ir.FindVar(name.Name) == null)
                DeclareHeapVar(name.Name, name.ResolvedSymbol.UdonType);

            return name.Name;
        }

        private string LowerBinary(BinaryExpr binary)
        {
            string leftVar = LowerExpr(binary.Left);
            string rightVar = LowerExpr(binary.Right);

            if (binary.ResolvedExtern == null) return AllocTemp("SystemObject");

            // Apply left conversion if needed (e.g., int -> float, or non-string -> string via ToString)
            if (binary.LeftConversion != null)
            {
                leftVar = EmitConversion(leftVar, binary.LeftConversion);
            }
            else if (binary.ResolvedExtern != null &&
                     binary.ResolvedExtern.Contains("Concat") &&
                     binary.Left.ResolvedType != "SystemString")
            {
                leftVar = EmitToString(leftVar);
            }

            // Apply right conversion if needed
            if (binary.RightConversion != null)
            {
                rightVar = EmitConversion(rightVar, binary.RightConversion);
            }
            else if (binary.ResolvedExtern != null &&
                     binary.ResolvedExtern.Contains("Concat") &&
                     binary.Right.ResolvedType != "SystemString")
            {
                rightVar = EmitToString(rightVar);
            }

            // Arrays derive from System.Array, not UnityEngine.Object.
            // The default op_Equality(UnityEngine.Object, UnityEngine.Object) causes a
            // heap type mismatch because the VM can't read a GameObject[] as UnityEngine.Object.
            // Use SystemObject.__Equals__(System.Object, System.Object) instead — System.Object
            // is the universal base type and accepts any heap variable type.
            if ((binary.Op == TokenKind.EqualsEquals || binary.Op == TokenKind.BangEquals)
                && HasArrayOperand(binary))
            {
                string eqResult = AllocTemp("SystemBoolean");
                Emit(new IrPush(leftVar));
                Emit(new IrPush(rightVar));
                Emit(new IrPush(eqResult));
                Emit(new IrExtern("SystemObject.__Equals__SystemObject_SystemObject__SystemBoolean"));

                if (binary.Op == TokenKind.BangEquals)
                {
                    string negResult = AllocTemp("SystemBoolean");
                    Emit(new IrPush(eqResult));
                    Emit(new IrPush(negResult));
                    Emit(new IrExtern("SystemBoolean.__op_UnaryNegation__SystemBoolean__SystemBoolean"));
                    return negResult;
                }
                return eqResult;
            }

            string resultType = binary.ResolvedType ?? "SystemObject";
            string resultVar = AllocTemp(resultType);

            Emit(new IrPush(leftVar));
            Emit(new IrPush(rightVar));
            Emit(new IrPush(resultVar));
            Emit(new IrExtern(binary.ResolvedExtern));

            return resultVar;
        }

        private string LowerUnary(UnaryExpr unary)
        {
            string operandVar = LowerExpr(unary.Operand);

            if (unary.ResolvedExtern == null) return AllocTemp("SystemObject");

            string resultType = unary.ResolvedType ?? "SystemObject";
            string resultVar = AllocTemp(resultType);

            Emit(new IrPush(operandVar));
            Emit(new IrPush(resultVar));
            Emit(new IrExtern(unary.ResolvedExtern));

            return resultVar;
        }

        private string LowerMemberAccess(MemberExpr member)
        {
            // Enum value access: store as SystemInt32 — Udon's assembler can't initialize
            // enum-typed variables from integer values in the data section, but externs
            // handle int→enum conversion at runtime.
            if (member.IsEnumValue)
            {
                return GetOrCreateConstant("SystemInt32", member.EnumIntValue.ToString());
            }

            // Static member access
            if (member.Object is NameExpr nameExpr &&
                (nameExpr.ResolvedSymbol?.Kind == SymbolKind.StaticType ||
                 nameExpr.ResolvedSymbol?.Kind == SymbolKind.EnumType))
            {
                if (member.ResolvedGetter != null)
                {
                    string resultType = member.ResolvedType ?? "SystemObject";
                    string resultVar = AllocTemp(resultType);
                    Emit(new IrPush(resultVar));
                    Emit(new IrExtern(member.ResolvedGetter.Extern));
                    return resultVar;
                }
            }

            // Instance member access
            string objVar = LowerExpr(member.Object);

            if (member.ResolvedGetter != null)
            {
                string resultType = member.ResolvedType ?? "SystemObject";
                string resultVar = AllocTemp(resultType);

                if (member.ResolvedGetter.IsInstance)
                {
                    Emit(new IrPush(objVar));
                }
                Emit(new IrPush(resultVar));
                Emit(new IrExtern(member.ResolvedGetter.Extern));
                return resultVar;
            }

            return objVar;
        }

        private string LowerCall(CallExpr call)
        {
            // Builtin calls
            if (call.IsBuiltinCall && call.Callee is NameExpr builtinName)
            {
                switch (builtinName.Name)
                {
                    case "log":
                        return LowerDebugLog(call, "UnityEngineDebug.__Log__SystemObject__SystemVoid");
                    case "warn":
                        return LowerDebugLog(call, "UnityEngineDebug.__LogWarning__SystemObject__SystemVoid");
                    case "error":
                        return LowerDebugLog(call, "UnityEngineDebug.__LogError__SystemObject__SystemVoid");
                    case "RequestSerialization":
                    {
                        Emit(new IrPush(_thisUdonBehaviour));
                        Emit(new IrExtern("VRCUdonCommonInterfacesIUdonEventReceiver.__RequestSerialization__SystemVoid"));
                        return GetOrCreateConstant("SystemObject", "null");
                    }
                    case "IsValid":
                    {
                        string validArg = call.Arguments.Count > 0
                            ? LowerExpr(call.Arguments[0])
                            : GetOrCreateConstant("SystemObject", "null");
                        string validResult = AllocTemp("SystemBoolean");
                        Emit(new IrPush(validArg));
                        Emit(new IrPush(validResult));
                        Emit(new IrExtern("VRCSDKBaseUtilities.__IsValid__UnityEngineObject__SystemBoolean"));
                        return validResult;
                    }
                    case "SendCustomEventDelayedSeconds":
                    {
                        Emit(new IrPush(_thisUdonBehaviour));
                        string eventArg = call.Arguments.Count > 0
                            ? LowerExpr(call.Arguments[0])
                            : GetOrCreateConstant("SystemString", "\"\"");
                        Emit(new IrPush(eventArg));
                        string delayArg = call.Arguments.Count > 1
                            ? LowerExpr(call.Arguments[1])
                            : GetOrCreateConstant("SystemSingle", "0");
                        Emit(new IrPush(delayArg));
                        string timingArg = call.Arguments.Count > 2
                            ? LowerExpr(call.Arguments[2])
                            : GetOrCreateConstant("SystemInt32", "0");
                        Emit(new IrPush(timingArg));
                        Emit(new IrExtern("VRCUdonCommonInterfacesIUdonEventReceiver.__SendCustomEventDelayedSeconds__SystemString_SystemSingle_VRCUdonCommonEnumsEventTiming__SystemVoid"));
                        return GetOrCreateConstant("SystemObject", "null");
                    }
                }
            }

            // Constructor calls: Color(...), Vector3(...), etc.
            if (call.IsConstructorCall && call.ResolvedExtern != null)
            {
                return LowerConstructorCall(call);
            }

            // User function calls
            if (call.ResolvedFunctionName != null)
            {
                return LowerFunctionCall(call);
            }

            // Method calls
            if (call.Callee is MemberExpr memberExpr && call.ResolvedExtern != null)
            {
                return LowerMethodCall(call, memberExpr);
            }

            return AllocTemp("SystemObject");
        }

        private string LowerDebugLog(CallExpr call, string externSig)
        {
            string argVar;
            if (call.Arguments.Count > 0)
            {
                argVar = LowerExpr(call.Arguments[0]);
            }
            else
            {
                argVar = GetOrCreateConstant("SystemString", "\"\"");
            }

            Emit(new IrPush(argVar));
            Emit(new IrExtern(externSig));
            return GetOrCreateConstant("SystemObject", "null");
        }

        private string LowerFunctionCall(CallExpr call)
        {
            string funcName = call.ResolvedFunctionName;

            // Copy arguments to parameter slots
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                string argVar = LowerExpr(call.Arguments[i]);
                // We need to look up the function declaration to get parameter names
                var funcDecl = _module.Declarations.OfType<FunctionDecl>()
                    .FirstOrDefault(f => f.Name == funcName);
                if (funcDecl != null && i < funcDecl.Parameters.Count)
                {
                    string paramSlot = $"__param_{funcName}_{funcDecl.Parameters[i].Name}";
                    Emit(new IrCopy(argVar, paramSlot));
                }
            }

            // Set return address (will be filled in by emitter)
            string retAddrVar = $"__retaddr_{funcName}";
            string returnLabel = NewLabel($"ret_{funcName}");
            string retAddrConst = GetOrCreateConstant("SystemUInt32", $"__label__{returnLabel}");
            Emit(new IrCopy(retAddrConst, retAddrVar));

            // Jump to function
            Emit(new IrJump($"__fn_{funcName}"));

            // Return point
            StartContinuationBlock(returnLabel);

            // Return value
            string retType = call.ResolvedType ?? "SystemVoid";
            if (retType != "SystemVoid")
            {
                string retValVar = $"__retval_{funcName}";
                string resultVar = AllocTemp(retType);
                Emit(new IrCopy(retValVar, resultVar));
                return resultVar;
            }

            return GetOrCreateConstant("SystemObject", "null");
        }

        // Methods that need 'this' UdonBehaviour auto-injected as an extra argument.
        // Uses Contains-based matching to work with both BuiltinCatalog and FullCatalog extern names.
        private static bool NeedsThisInjection(string externName)
        {
            return externName.Contains("__LoadUrl__") || externName.Contains("__DownloadImage__");
        }

        private string LowerMethodCall(CallExpr call, MemberExpr memberExpr)
        {
            var sig = call.ResolvedExtern;

            // Switch GetComponent/GetComponentsInChildren from SystemType to SystemString overload.
            // Udon's assembler can't initialize SystemType constants in the data section,
            // but the string-based overload works correctly.
            string externName = sig.Extern;
            if (externName.Contains("__SystemType__"))
                externName = externName.Replace("__SystemType__", "__SystemString__");

            // For instance methods, push 'this' object first
            if (sig.IsInstance)
            {
                string objVar = LowerExpr(memberExpr.Object);
                Emit(new IrPush(objVar));
            }

            // Push arguments (with implicit conversions if needed)
            bool needsThisInjection = NeedsThisInjection(externName);
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                string argVar = LowerExpr(call.Arguments[i]);

                // Apply implicit conversion if annotated
                if (call.ImplicitConversions != null &&
                    i < call.ImplicitConversions.Count &&
                    call.ImplicitConversions[i] != null)
                {
                    argVar = EmitConversion(argVar, call.ImplicitConversions[i]);
                }

                Emit(new IrPush(argVar));

                // For LoadUrl: after first arg (VRCUrl), auto-inject this UdonBehaviour
                // For DownloadImage: after second arg (Material), auto-inject this UdonBehaviour
                if (needsThisInjection)
                {
                    bool injectAfterThis =
                        (externName.Contains("LoadUrl") && i == 0) ||
                        (externName.Contains("DownloadImage") && i == 1);
                    if (injectAfterThis)
                        Emit(new IrPush(_thisUdonBehaviour));
                }
            }

            // Push result only for non-void returns
            string resultType = sig.ReturnType ?? "SystemVoid";
            if (resultType != "SystemVoid")
            {
                string resultVar = AllocTemp(resultType);
                Emit(new IrPush(resultVar));
                Emit(new IrExtern(externName));
                return resultVar;
            }

            Emit(new IrExtern(externName));
            return GetOrCreateConstant("SystemObject", "null");
        }

        private string LowerCast(CastExpr cast)
        {
            string srcVar = LowerExpr(cast.Operand);
            string targetType = cast.ResolvedType ?? TypeSystem.ResolveType(cast.TargetTypeName) ?? "SystemObject";
            string srcType = cast.Operand.ResolvedType ?? "SystemObject";

            // Try implicit conversion first
            var conversion = _catalog.GetImplicitConversion(srcType, targetType);
            if (conversion != null)
                return EmitConversion(srcVar, conversion);

            // Fallback: use SystemConvert for numeric types
            string convertExtern = GetConvertExtern(srcType, targetType);
            if (convertExtern != null)
            {
                string resultVar = AllocTemp(targetType);
                Emit(new IrPush(srcVar));
                Emit(new IrPush(resultVar));
                Emit(new IrExtern(convertExtern));
                return resultVar;
            }

            // No conversion available, just return source (may be a reference cast)
            return srcVar;
        }

        private static string GetConvertExtern(string fromType, string toType)
        {
            // Map numeric conversions to SystemConvert externs
            if (fromType == "SystemDouble" && toType == "SystemSingle")
                return "SystemConvert.__ToSingle__SystemDouble__SystemSingle";
            if (fromType == "SystemDouble" && toType == "SystemInt32")
                return "SystemConvert.__ToInt32__SystemDouble__SystemInt32";
            if (fromType == "SystemSingle" && toType == "SystemInt32")
                return "SystemConvert.__ToInt32__SystemSingle__SystemInt32";
            if (fromType == "SystemInt32" && toType == "SystemSingle")
                return "SystemConvert.__ToSingle__SystemInt32__SystemSingle";
            if (fromType == "SystemInt32" && toType == "SystemDouble")
                return "SystemConvert.__ToDouble__SystemInt32__SystemDouble";
            if (fromType == "SystemSingle" && toType == "SystemDouble")
                return "SystemConvert.__ToDouble__SystemSingle__SystemDouble";
            return null;
        }

        private string LowerConstructorCall(CallExpr call)
        {
            var sig = call.ResolvedExtern;

            // Push arguments with implicit conversions
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                string argVar = LowerExpr(call.Arguments[i]);

                if (call.ImplicitConversions != null &&
                    i < call.ImplicitConversions.Count &&
                    call.ImplicitConversions[i] != null)
                {
                    argVar = EmitConversion(argVar, call.ImplicitConversions[i]);
                }

                Emit(new IrPush(argVar));
            }

            string resultType = sig.ReturnType ?? call.ResolvedType ?? "SystemObject";
            string resultVar = AllocTemp(resultType);
            Emit(new IrPush(resultVar));
            Emit(new IrExtern(sig.Extern));
            return resultVar;
        }

        private string LowerIndex(IndexExpr index)
        {
            // Array construction: Type[size] — e.g., VRCPlayerApi[8]
            if (index.IsArrayConstruction)
            {
                string sizeVar = LowerExpr(index.Index);
                string arrayType = index.ResolvedType ?? "SystemObjectArray";
                string arrayVar = AllocTemp(arrayType);
                Emit(new IrPush(sizeVar));
                Emit(new IrPush(arrayVar));
                Emit(new IrExtern($"{arrayType}.__ctor__SystemInt32__{arrayType}"));
                return arrayVar;
            }

            string arrayVar2 = LowerExpr(index.Object);
            string indexVar = LowerExpr(index.Index);

            string arrayType2 = index.Object.ResolvedType ?? "SystemObjectArray";
            string elemType = arrayType2.EndsWith("Array")
                ? arrayType2.Substring(0, arrayType2.Length - 5) : "SystemObject";

            string getExtern = $"{arrayType2}.__Get__SystemInt32__{elemType}";
            string resultVar = AllocTemp(elemType);

            Emit(new IrPush(arrayVar2));
            Emit(new IrPush(indexVar));
            Emit(new IrPush(resultVar));
            Emit(new IrExtern(getExtern));

            return resultVar;
        }

        private string LowerArrayLiteral(ArrayLiteralExpr arr)
        {
            string arrayType = arr.ResolvedType ?? "SystemObjectArray";
            string elemType = arrayType.EndsWith("Array")
                ? arrayType.Substring(0, arrayType.Length - 5) : "SystemObject";

            // Create the array: push size, push result slot, call ctor
            string sizeConst = GetOrCreateConstant("SystemInt32", arr.Elements.Count.ToString());
            string arrayVar = AllocTemp(arrayType);
            Emit(new IrPush(sizeConst));
            Emit(new IrPush(arrayVar));
            Emit(new IrExtern($"{arrayType}.__ctor__SystemInt32__{arrayType}"));

            // Populate each element
            string setExtern = $"{arrayType}.__Set__SystemInt32_{elemType}__SystemVoid";
            for (int i = 0; i < arr.Elements.Count; i++)
            {
                string elemVar = LowerExpr(arr.Elements[i]);
                string idxConst = GetOrCreateConstant("SystemInt32", i.ToString());
                Emit(new IrPush(arrayVar));
                Emit(new IrPush(idxConst));
                Emit(new IrPush(elemVar));
                Emit(new IrExtern(setExtern));
            }

            return arrayVar;
        }

        // --- Helpers ---

        private string DeclareHeapVar(string name, string udonType, string initialValue = null)
        {
            if (_ir.FindVar(name) != null)
            {
                // Variable already exists, generate a unique name
                string uniqueName = $"__lcl_{name}_{udonType}_{_tempCounter++}";
                _ir.HeapVars.Add(new IrHeapVar(uniqueName, udonType, initialValue));
                return uniqueName;
            }

            _ir.HeapVars.Add(new IrHeapVar(name, udonType, initialValue));
            return name;
        }

        private string AllocTemp(string udonType)
        {
            string name = $"__tmp_{_tempCounter++}_{udonType}";
            _ir.HeapVars.Add(new IrHeapVar(name, udonType));
            return name;
        }

        private string GetOrCreateConstant(string udonType, string value)
        {
            var key = (udonType, value);
            if (_constants.TryGetValue(key, out var existing))
                return existing;

            string name = $"__const_{_tempCounter++}_{udonType}";
            _ir.HeapVars.Add(new IrHeapVar(name, udonType, value));

            _constants[key] = name;
            return name;
        }

        private string NewLabel(string prefix)
        {
            return $"__{prefix}_{_labelCounter++}";
        }

        private void Emit(IrInstruction instr)
        {
            _currentBlock.Instructions.Add(instr);
        }

        private void StartContinuationBlock(string label)
        {
            var block = new IrBlock(label);
            _ir.Blocks.Add(block);
            _currentBlock = block;
        }

        private string GetInitialValue(Expr initializer, string udonType)
        {
            if (initializer == null)
                return TypeSystem.DefaultValue(udonType);

            switch (initializer)
            {
                case IntLiteralExpr intLit:
                    return intLit.Value.ToString();
                case FloatLiteralExpr floatLit:
                    return floatLit.Value.ToString(CultureInfo.InvariantCulture);
                case BoolLiteralExpr _:
                    // VRC's parser only accepts null for booleans in the data section.
                    // Actual value is set at runtime via COPY.
                    return "null";
                case StringLiteralExpr strLit:
                    return $"\"{EscapeString(strLit.Value)}\"";
                case NullLiteralExpr _:
                    return "null";
                default:
                    return TypeSystem.DefaultValue(udonType);
            }
        }

        private static string EscapeString(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\t", "\\t");
        }

        private static TokenKind AssignOpToTokenKind(AssignOp op)
        {
            switch (op)
            {
                case AssignOp.AddAssign: return TokenKind.Plus;
                case AssignOp.SubAssign: return TokenKind.Minus;
                case AssignOp.MulAssign: return TokenKind.Star;
                case AssignOp.DivAssign: return TokenKind.Slash;
                default: return TokenKind.Plus;
            }
        }

        /// <summary>
        /// Emits an implicit conversion (e.g., int -> float via SystemConvert).
        /// Returns the name of the converted temp var.
        /// </summary>
        private string EmitToString(string inputVar)
        {
            string strVar = AllocTemp("SystemString");
            Emit(new IrPush(inputVar));
            Emit(new IrPush(strVar));
            Emit(new IrExtern("SystemObject.__ToString__SystemString"));
            return strVar;
        }

        private string EmitConversion(string inputVar, ImplicitConversion conversion)
        {
            string convertedVar = AllocTemp(conversion.ToType);
            Emit(new IrPush(inputVar));
            Emit(new IrPush(convertedVar));
            Emit(new IrExtern(conversion.ConversionExtern));
            return convertedVar;
        }

        private static bool HasArrayOperand(BinaryExpr binary)
        {
            string leftType = binary.Left.ResolvedType;
            string rightType = binary.Right.ResolvedType;
            return (leftType != null && leftType.EndsWith("Array")) ||
                   (rightType != null && rightType.EndsWith("Array"));
        }
    }
}
