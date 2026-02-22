using System.Collections.Generic;
using System.Linq;

namespace Nori.Compiler
{
    public class SemanticAnalyzer
    {
        private readonly ModuleDecl _module;
        private readonly IExternCatalog _catalog;
        private readonly DiagnosticBag _diagnostics;
        private Scope _currentScope;
        private string _currentFunction; // null if in event/top-level
        private string _currentFunctionReturnType; // null if void
        private bool _inLoop;

        // Call graph for recursion detection
        private readonly Dictionary<string, HashSet<string>> _callGraph
            = new Dictionary<string, HashSet<string>>();

        // LSP support: maps from AST node to resolved type and active scope
        private readonly Dictionary<AstNode, string> _typeMap = new Dictionary<AstNode, string>();
        private readonly Dictionary<AstNode, Scope> _scopeMap = new Dictionary<AstNode, Scope>();

        /// <summary>Get the type map populated during analysis (node -> resolved Udon type).</summary>
        public Dictionary<AstNode, string> GetTypeMap() => _typeMap;

        /// <summary>Get the scope map populated during analysis (node -> active scope at that node).</summary>
        public Dictionary<AstNode, Scope> GetScopeMap() => _scopeMap;

        // Event name -> Udon label mapping
        private static readonly Dictionary<string, string> EventNameMap = new Dictionary<string, string>
        {
            ["Start"] = "_start",
            ["Enable"] = "_onEnable",
            ["Disable"] = "_onDisable",
            ["Update"] = "_update",
            ["LateUpdate"] = "_lateUpdate",
            ["FixedUpdate"] = "_fixedUpdate",
            ["Interact"] = "_interact",
            ["Pickup"] = "_onPickup",
            ["Drop"] = "_onDrop",
            ["PickupUseDown"] = "_onPickupUseDown",
            ["PickupUseUp"] = "_onPickupUseUp",
            ["PlayerJoined"] = "_onPlayerJoined",
            ["PlayerLeft"] = "_onPlayerLeft",
            ["TriggerEnter"] = "_onTriggerEnter",
            ["TriggerExit"] = "_onTriggerExit",
            ["CollisionEnter"] = "_onCollisionEnter",
            ["VariableChange"] = "_onDeserialization",
            ["PreSerialization"] = "_onPreSerialization",
            ["PostSerialization"] = "_onPostSerialization",
            ["InputJump"] = "_inputJump",
            ["InputUse"] = "_inputUse",
            ["InputGrab"] = "_inputGrab",
            ["InputDrop"] = "_inputDrop",
            ["InputMoveHorizontal"] = "_inputMoveHorizontal",
            ["InputMoveVertical"] = "_inputMoveVertical",
            ["InputLookHorizontal"] = "_inputLookHorizontal",
            ["InputLookVertical"] = "_inputLookVertical",
            ["MouseDown"] = "_onMouseDown",
            ["Destroy"] = "_onDestroy",
            ["CollisionExit"] = "_onCollisionExit",
            ["PlayerTriggerEnter"] = "_onPlayerTriggerEnter",
            ["PlayerTriggerExit"] = "_onPlayerTriggerExit",
            ["PlayerCollisionEnter"] = "_onPlayerCollisionEnter",
            ["PlayerCollisionExit"] = "_onPlayerCollisionExit",
            ["PlayerParticleCollision"] = "_onPlayerParticleCollision",
            ["Deserialization"] = "_onDeserialization",
            ["OwnershipRequest"] = "_onOwnershipRequest",
            ["OwnershipTransferred"] = "_onOwnershipTransferred",
            ["VideoStart"] = "_onVideoStart",
            ["AvatarEyeHeightChanged"] = "_onAvatarEyeHeightChanged",
            ["StringLoadSuccess"] = "_onStringLoadSuccess",
            ["StringLoadError"] = "_onStringLoadError",
            ["ImageLoadSuccess"] = "_onImageLoadSuccess",
            ["ImageLoadError"] = "_onImageLoadError",
        };

        public SemanticAnalyzer(ModuleDecl module, IExternCatalog catalog, DiagnosticBag diagnostics)
        {
            _module = module;
            _catalog = catalog;
            _diagnostics = diagnostics;
        }

        public ModuleDecl Analyze()
        {
            _currentScope = new Scope();

            // Register builtins
            RegisterBuiltins();

            // First pass: register all top-level declarations
            foreach (var decl in _module.Declarations)
            {
                RegisterDeclaration(decl);
            }

            // Second pass: analyze bodies
            foreach (var decl in _module.Declarations)
            {
                AnalyzeDeclaration(decl);
            }

            // Check for recursion
            DetectRecursion();

            return _module;
        }

        private void RegisterBuiltins()
        {
            // Set catalog reference for TypeSystem fallback resolution
            TypeSystem.Catalog = _catalog;

            _currentScope.Define(new Symbol("localPlayer", "VRCSDKBaseVRCPlayerApi",
                SourceSpan.None, SymbolKind.Builtin));
            _currentScope.Define(new Symbol("gameObject", "UnityEngineGameObject",
                SourceSpan.None, SymbolKind.Builtin));
            _currentScope.Define(new Symbol("transform", "UnityEngineTransform",
                SourceSpan.None, SymbolKind.Builtin));

            // Builtin functions
            _currentScope.Define(new Symbol("log", "SystemVoid",
                SourceSpan.None, SymbolKind.Builtin));
            _currentScope.Define(new Symbol("warn", "SystemVoid",
                SourceSpan.None, SymbolKind.Builtin));
            _currentScope.Define(new Symbol("error", "SystemVoid",
                SourceSpan.None, SymbolKind.Builtin));
            _currentScope.Define(new Symbol("RequestSerialization", "SystemVoid",
                SourceSpan.None, SymbolKind.Builtin));
            _currentScope.Define(new Symbol("IsValid", "SystemBoolean",
                SourceSpan.None, SymbolKind.Builtin));
            _currentScope.Define(new Symbol("SendCustomEventDelayedSeconds", "SystemVoid",
                SourceSpan.None, SymbolKind.Builtin));

            // Hardcoded static type names (always available)
            _currentScope.Define(new Symbol("Time", "UnityEngineTime",
                SourceSpan.None, SymbolKind.StaticType));
            _currentScope.Define(new Symbol("Networking", "VRCSDKBaseNetworking",
                SourceSpan.None, SymbolKind.StaticType));
            _currentScope.Define(new Symbol("Vector3", "UnityEngineVector3",
                SourceSpan.None, SymbolKind.StaticType));
            _currentScope.Define(new Symbol("Vector2", "UnityEngineVector2",
                SourceSpan.None, SymbolKind.StaticType));
            _currentScope.Define(new Symbol("Quaternion", "UnityEngineQuaternion",
                SourceSpan.None, SymbolKind.StaticType));
            _currentScope.Define(new Symbol("Mathf", "UnityEngineMathf",
                SourceSpan.None, SymbolKind.StaticType));
            _currentScope.Define(new Symbol("Color", "UnityEngineColor",
                SourceSpan.None, SymbolKind.StaticType));
            _currentScope.Define(new Symbol("Random", "UnityEngineRandom",
                SourceSpan.None, SymbolKind.StaticType));
            _currentScope.Define(new Symbol("Input", "UnityEngineInput",
                SourceSpan.None, SymbolKind.StaticType));
            _currentScope.Define(new Symbol("String", "SystemString",
                SourceSpan.None, SymbolKind.StaticType));
            _currentScope.Define(new Symbol("Physics", "UnityEnginePhysics",
                SourceSpan.None, SymbolKind.StaticType));
            _currentScope.Define(new Symbol("Vector4", "UnityEngineVector4",
                SourceSpan.None, SymbolKind.StaticType));
            _currentScope.Define(new Symbol("VRCPlayerApi", "VRCSDKBaseVRCPlayerApi",
                SourceSpan.None, SymbolKind.StaticType));
            _currentScope.Define(new Symbol("Utilities", "VRCSDKBaseUtilities",
                SourceSpan.None, SymbolKind.StaticType));
            _currentScope.Define(new Symbol("VRCStringDownloader", "VRCSDKBaseVRC_StringDownloader",
                SourceSpan.None, SymbolKind.StaticType));
            _currentScope.Define(new Symbol("VRCImageDownloader", "VRCSDK3ImageVRCImageDownloader",
                SourceSpan.None, SymbolKind.StaticType));

            // Dynamic registration from catalog: register all types with static members
            if (_catalog is FullCatalog fullCatalog)
            {
                foreach (var kv in fullCatalog.GetShortNameMappings())
                {
                    string shortName = kv.Key;
                    string udonType = kv.Value;

                    // Don't override existing registrations
                    if (_currentScope.Lookup(shortName) != null)
                        continue;

                    // Determine if enum or static type
                    if (_catalog.IsEnumType(udonType))
                    {
                        _currentScope.Define(new Symbol(shortName, udonType,
                            SourceSpan.None, SymbolKind.EnumType));
                    }
                    else
                    {
                        _currentScope.Define(new Symbol(shortName, udonType,
                            SourceSpan.None, SymbolKind.StaticType));
                    }
                }
            }
        }

        private void RegisterDeclaration(Decl decl)
        {
            switch (decl)
            {
                case VarDecl v:
                {
                    string udonType = ResolveTypeName(v.TypeName, v.IsArray, v.Span);
                    var sym = new Symbol(v.Name, udonType, v.Span, SymbolKind.Variable)
                    {
                        IsPublic = v.IsPublic,
                        SyncMode = v.SyncMode,
                        IsArray = v.IsArray,
                    };
                    if (!_currentScope.Define(sym))
                    {
                        _diagnostics.ReportError("E0070",
                            $"Variable '{v.Name}' is already defined", v.Span);
                    }
                    break;
                }
                case FunctionDecl f:
                {
                    string retType = f.ReturnTypeName != null
                        ? ResolveTypeName(f.ReturnTypeName, false, f.Span) : "SystemVoid";
                    var sym = new Symbol(f.Name, retType, f.Span, SymbolKind.Function);
                    if (!_currentScope.Define(sym))
                    {
                        _diagnostics.ReportError("E0071",
                            $"Function '{f.Name}' is already defined", f.Span);
                    }
                    _callGraph[f.Name] = new HashSet<string>();
                    break;
                }
                case CustomEventDecl ce:
                {
                    var sym = new Symbol(ce.Name, "SystemVoid", ce.Span, SymbolKind.CustomEvent);
                    _currentScope.Define(sym);
                    break;
                }
            }
        }

        private void AnalyzeDeclaration(Decl decl)
        {
            _scopeMap[decl] = _currentScope;

            switch (decl)
            {
                case VarDecl v:
                    if (v.Initializer != null)
                        AnalyzeExpr(v.Initializer);
                    break;

                case EventHandlerDecl e:
                    AnalyzeEventHandler(e);
                    break;

                case CustomEventDecl ce:
                    _scopeMap[ce] = _currentScope;
                    AnalyzeBlock(ce.Body);
                    break;

                case FunctionDecl f:
                    AnalyzeFunction(f);
                    break;
            }
        }

        // Implicit event parameters — VRC events that receive data automatically
        private static readonly Dictionary<string, (string paramName, string paramType)> EventImplicitParams
            = new Dictionary<string, (string, string)>
        {
            ["StringLoadSuccess"] = ("result", "VRCSDK3StringLoadingIVRCStringDownload"),
            ["StringLoadError"] = ("result", "VRCSDK3StringLoadingIVRCStringDownload"),
            ["ImageLoadSuccess"] = ("result", "VRCSDK3ImageIVRCImageDownload"),
            ["ImageLoadError"] = ("result", "VRCSDK3ImageIVRCImageDownload"),
        };

        private void AnalyzeEventHandler(EventHandlerDecl handler)
        {
            if (!EventNameMap.ContainsKey(handler.EventName))
            {
                _diagnostics.ReportWarning("W0010",
                    $"Unknown event '{handler.EventName}'", handler.Span,
                    "This event name is not recognized as a standard VRChat event.");
            }

            var scope = new Scope(_currentScope);
            var prevScope = _currentScope;
            _currentScope = scope;
            _scopeMap[handler] = scope;

            foreach (var param in handler.Parameters)
            {
                string paramType = ResolveTypeName(param.TypeName, false, param.Span);
                scope.Define(new Symbol(param.Name, paramType, param.Span, SymbolKind.Parameter));
                _scopeMap[param] = scope;
            }

            // Define implicit event parameters (e.g., 'result' for download events)
            if (EventImplicitParams.TryGetValue(handler.EventName, out var implicitParam))
            {
                if (scope.Lookup(implicitParam.paramName) == null)
                {
                    scope.Define(new Symbol(implicitParam.paramName, implicitParam.paramType,
                        SourceSpan.None, SymbolKind.Parameter));
                }
            }

            AnalyzeBlock(handler.Body);
            _currentScope = prevScope;
        }

        private void AnalyzeFunction(FunctionDecl func)
        {
            var scope = new Scope(_currentScope);
            var prevScope = _currentScope;
            var prevFunction = _currentFunction;
            var prevReturnType = _currentFunctionReturnType;

            _currentScope = scope;
            _currentFunction = func.Name;
            _currentFunctionReturnType = func.ReturnTypeName != null
                ? ResolveTypeName(func.ReturnTypeName, false, func.Span) : null;
            _scopeMap[func] = scope;

            foreach (var param in func.Parameters)
            {
                string paramType = ResolveTypeName(param.TypeName, false, param.Span);
                scope.Define(new Symbol(param.Name, paramType, param.Span, SymbolKind.Parameter));
                _scopeMap[param] = scope;
            }

            AnalyzeBlock(func.Body);

            _currentScope = prevScope;
            _currentFunction = prevFunction;
            _currentFunctionReturnType = prevReturnType;
        }

        private void AnalyzeBlock(List<Stmt> stmts)
        {
            foreach (var stmt in stmts)
                AnalyzeStmt(stmt);
        }

        private void AnalyzeStmt(Stmt stmt)
        {
            _scopeMap[stmt] = _currentScope;

            switch (stmt)
            {
                case LocalVarStmt lv:
                {
                    string udonType = ResolveTypeName(lv.TypeName, lv.IsArray, lv.Span);
                    var sym = new Symbol(lv.Name, udonType, lv.Span, SymbolKind.Variable)
                    {
                        IsArray = lv.IsArray,
                    };
                    _currentScope.Define(sym);
                    if (lv.Initializer != null)
                    {
                        AnalyzeExpr(lv.Initializer);
                        if (lv.Initializer.ResolvedType != null && udonType != null &&
                            !TypeSystem.IsAssignable(udonType, lv.Initializer.ResolvedType))
                        {
                            _diagnostics.ReportTypeMismatch(
                                TypeSystem.ToNoriType(udonType),
                                TypeSystem.ToNoriType(lv.Initializer.ResolvedType),
                                lv.Span);
                        }
                    }
                    break;
                }
                case AssignStmt assign:
                {
                    AnalyzeExpr(assign.Target);
                    AnalyzeExpr(assign.Value);

                    if (assign.Target.ResolvedType != null && assign.Value.ResolvedType != null)
                    {
                        string targetType = assign.Target.ResolvedType;
                        string valueType = assign.Value.ResolvedType;

                        // For compound assignment, check that the operation is valid
                        if (assign.Op != AssignOp.Assign)
                        {
                            TokenKind opKind = AssignOpToTokenKind(assign.Op);
                            var opInfo = _catalog.ResolveOperator(opKind, targetType, valueType);
                            if (opInfo == null)
                            {
                                _diagnostics.ReportTypeMismatch(
                                    TypeSystem.ToNoriType(targetType),
                                    TypeSystem.ToNoriType(valueType),
                                    assign.Span);
                            }
                            else
                            {
                                assign.ResolvedOperator = opInfo;
                            }
                        }
                        else if (!TypeSystem.IsAssignable(targetType, valueType))
                        {
                            _diagnostics.ReportTypeMismatch(
                                TypeSystem.ToNoriType(targetType),
                                TypeSystem.ToNoriType(valueType),
                                assign.Span);
                        }
                    }

                    // Check property writeability for MemberExpr targets
                    if (assign.Target is MemberExpr memberTarget &&
                        memberTarget.ResolvedGetter != null &&
                        memberTarget.ResolvedSetter == null)
                    {
                        _diagnostics.ReportPropertyNotWritable(memberTarget.MemberName, assign.Span);
                    }
                    break;
                }
                case IfStmt ifStmt:
                {
                    AnalyzeExpr(ifStmt.Condition);
                    if (ifStmt.Condition.ResolvedType != null &&
                        ifStmt.Condition.ResolvedType != "SystemBoolean")
                    {
                        _diagnostics.ReportTypeMismatch("bool",
                            TypeSystem.ToNoriType(ifStmt.Condition.ResolvedType),
                            ifStmt.Condition.Span);
                    }
                    AnalyzeBlock(ifStmt.ThenBody);
                    if (ifStmt.ElseBody != null)
                        AnalyzeBlock(ifStmt.ElseBody);
                    break;
                }
                case WhileStmt whileStmt:
                {
                    AnalyzeExpr(whileStmt.Condition);
                    if (whileStmt.Condition.ResolvedType != null &&
                        whileStmt.Condition.ResolvedType != "SystemBoolean")
                    {
                        _diagnostics.ReportTypeMismatch("bool",
                            TypeSystem.ToNoriType(whileStmt.Condition.ResolvedType),
                            whileStmt.Condition.Span);
                    }
                    bool prevInLoop = _inLoop;
                    _inLoop = true;
                    AnalyzeBlock(whileStmt.Body);
                    _inLoop = prevInLoop;
                    break;
                }
                case ForRangeStmt forRange:
                {
                    AnalyzeExpr(forRange.Start);
                    AnalyzeExpr(forRange.End);

                    var scope = new Scope(_currentScope);
                    var prevScope = _currentScope;
                    _currentScope = scope;

                    scope.Define(new Symbol(forRange.VarName, "SystemInt32",
                        forRange.Span, SymbolKind.Variable));

                    bool prevInLoop = _inLoop;
                    _inLoop = true;
                    AnalyzeBlock(forRange.Body);
                    _inLoop = prevInLoop;

                    _currentScope = prevScope;
                    break;
                }
                case ForEachStmt forEach:
                {
                    AnalyzeExpr(forEach.Collection);

                    string elemType = "SystemObject";
                    if (forEach.Collection.ResolvedType != null &&
                        forEach.Collection.ResolvedType.EndsWith("Array"))
                    {
                        elemType = forEach.Collection.ResolvedType
                            .Substring(0, forEach.Collection.ResolvedType.Length - 5);
                    }

                    var scope = new Scope(_currentScope);
                    var prevScope = _currentScope;
                    _currentScope = scope;

                    scope.Define(new Symbol(forEach.VarName, elemType,
                        forEach.Span, SymbolKind.Variable));

                    bool prevInLoop = _inLoop;
                    _inLoop = true;
                    AnalyzeBlock(forEach.Body);
                    _inLoop = prevInLoop;

                    _currentScope = prevScope;
                    break;
                }
                case ReturnStmt ret:
                {
                    if (ret.Value != null)
                    {
                        AnalyzeExpr(ret.Value);
                        if (_currentFunctionReturnType != null &&
                            ret.Value.ResolvedType != null &&
                            !TypeSystem.IsAssignable(_currentFunctionReturnType, ret.Value.ResolvedType))
                        {
                            _diagnostics.ReportTypeMismatch(
                                TypeSystem.ToNoriType(_currentFunctionReturnType),
                                TypeSystem.ToNoriType(ret.Value.ResolvedType),
                                ret.Span);
                        }
                    }
                    break;
                }
                case BreakStmt brk:
                    if (!_inLoop)
                    {
                        _diagnostics.ReportError("E0101", "'break' is only valid inside a loop",
                            brk.Span);
                    }
                    break;
                case ContinueStmt cont:
                    if (!_inLoop)
                    {
                        _diagnostics.ReportError("E0102", "'continue' is only valid inside a loop",
                            cont.Span);
                    }
                    break;
                case SendStmt send:
                {
                    // Verify the event exists (custom events or functions are valid targets)
                    var sym = _currentScope.Lookup(send.EventName);
                    if (sym == null || (sym.Kind != SymbolKind.CustomEvent && sym.Kind != SymbolKind.Function))
                    {
                        _diagnostics.ReportError("E0071",
                            $"Undefined custom event '{send.EventName}'", send.Span);
                    }
                    break;
                }
                case ExpressionStmt exprStmt:
                    AnalyzeExpr(exprStmt.Expression);
                    break;
            }
        }

        private void AnalyzeExpr(Expr expr)
        {
            _scopeMap[expr] = _currentScope;

            switch (expr)
            {
                case IntLiteralExpr _:
                case FloatLiteralExpr _:
                case BoolLiteralExpr _:
                case StringLiteralExpr _:
                case NullLiteralExpr _:
                    break; // types already set

                case InterpolatedStringExpr interp:
                    foreach (var part in interp.Parts)
                        AnalyzeExpr(part);
                    break;

                case NameExpr name:
                    ResolveNameExpr(name);
                    break;

                case BinaryExpr binary:
                    AnalyzeBinaryExpr(binary);
                    break;

                case UnaryExpr unary:
                    AnalyzeUnaryExpr(unary);
                    break;

                case MemberExpr member:
                    AnalyzeMemberExpr(member);
                    break;

                case CallExpr call:
                    AnalyzeCallExpr(call);
                    break;

                case IndexExpr index:
                    AnalyzeIndexExpr(index);
                    break;

                case ArrayLiteralExpr arr:
                    foreach (var elem in arr.Elements)
                        AnalyzeExpr(elem);
                    if (arr.Elements.Count > 0 && arr.Elements[0].ResolvedType != null)
                        arr.ResolvedType = arr.Elements[0].ResolvedType + "Array";
                    break;

                case CastExpr cast:
                    AnalyzeExpr(cast.Operand);
                    string castTarget = TypeSystem.ResolveType(cast.TargetTypeName);
                    if (castTarget == null)
                        _diagnostics.ReportError("E0040",
                            $"Unknown type '{cast.TargetTypeName}'", cast.Span);
                    else
                        cast.ResolvedType = castTarget;
                    break;
            }

            // Track resolved type for LSP
            if (expr.ResolvedType != null)
                _typeMap[expr] = expr.ResolvedType;
        }

        private void ResolveNameExpr(NameExpr name)
        {
            var sym = _currentScope.Lookup(name.Name);
            if (sym == null)
            {
                // Try resolving as a type name (for type-as-value usage like GetComponent(MeshRenderer))
                string udonType = TypeSystem.ResolveType(name.Name);
                if (udonType != null)
                {
                    var kind = _catalog.IsEnumType(udonType) ? SymbolKind.EnumType : SymbolKind.StaticType;
                    sym = new Symbol(name.Name, udonType, SourceSpan.None, kind);
                    _currentScope.Define(sym);
                }
                else
                {
                    string suggestion = _currentScope.FindClosest(name.Name);
                    _diagnostics.ReportUndefinedVariable(name.Name, name.Span, suggestion);
                    return;
                }
            }

            name.ResolvedSymbol = sym;
            name.ResolvedType = sym.UdonType;
        }

        private void AnalyzeBinaryExpr(BinaryExpr binary)
        {
            AnalyzeExpr(binary.Left);
            AnalyzeExpr(binary.Right);

            if (binary.Left.ResolvedType == null || binary.Right.ResolvedType == null)
                return;

            string leftType = binary.Left.ResolvedType;
            string rightType = binary.Right.ResolvedType;

            var opInfo = _catalog.ResolveOperator(binary.Op, leftType, rightType);

            if (opInfo == null)
            {
                _diagnostics.ReportError("E0040",
                    $"Operator '{TokenKindToString(binary.Op)}' cannot be applied to " +
                    $"'{TypeSystem.ToNoriType(leftType)}' and " +
                    $"'{TypeSystem.ToNoriType(rightType)}'",
                    binary.Span);
                return;
            }

            binary.ResolvedExtern = opInfo.Extern;
            binary.ResolvedType = opInfo.ReturnType;

            // Annotate widening conversions if operand types differ from operator's expected types
            if (opInfo.LeftType != null && opInfo.LeftType != leftType)
            {
                binary.LeftConversion = _catalog.GetImplicitConversion(leftType, opInfo.LeftType);
            }
            if (opInfo.RightType != null && opInfo.RightType != rightType)
            {
                binary.RightConversion = _catalog.GetImplicitConversion(rightType, opInfo.RightType);
            }
        }

        private void AnalyzeUnaryExpr(UnaryExpr unary)
        {
            AnalyzeExpr(unary.Operand);

            if (unary.Operand.ResolvedType == null) return;

            var opInfo = _catalog.ResolveUnaryOperator(unary.Op, unary.Operand.ResolvedType);
            if (opInfo == null)
            {
                _diagnostics.ReportError("E0040",
                    $"Unary operator '{TokenKindToString(unary.Op)}' cannot be applied to " +
                    $"'{TypeSystem.ToNoriType(unary.Operand.ResolvedType)}'",
                    unary.Span);
                return;
            }

            unary.ResolvedExtern = opInfo.Extern;
            unary.ResolvedType = opInfo.ReturnType;
        }

        private void AnalyzeMemberExpr(MemberExpr member)
        {
            AnalyzeExpr(member.Object);

            if (member.Object.ResolvedType == null) return;

            string ownerType = member.Object.ResolvedType;

            // Check if this is a static type or enum type access
            if (member.Object is NameExpr nameExpr)
            {
                if (nameExpr.ResolvedSymbol?.Kind == SymbolKind.StaticType)
                {
                    ownerType = nameExpr.ResolvedSymbol.UdonType;
                }
                else if (nameExpr.ResolvedSymbol?.Kind == SymbolKind.EnumType)
                {
                    // Enum value access: e.g., Space.Self
                    ownerType = nameExpr.ResolvedSymbol.UdonType;
                    var enumInfo = _catalog.ResolveEnum(ownerType);
                    if (enumInfo != null)
                    {
                        if (enumInfo.Values.TryGetValue(member.MemberName, out int intVal))
                        {
                            member.IsEnumValue = true;
                            member.EnumIntValue = intVal;
                            member.EnumUdonType = ownerType;
                            member.ResolvedType = ownerType;
                            return;
                        }
                        _diagnostics.ReportEnumValueNotFound(member.MemberName, ownerType, member.Span);
                        return;
                    }
                }
            }

            var prop = _catalog.ResolveProperty(ownerType, member.MemberName);
            if (prop != null)
            {
                member.ResolvedType = prop.Type;
                member.ResolvedGetter = prop.Getter;
                member.ResolvedSetter = prop.Setter;
                return;
            }

            // Maybe it's a method - will be resolved during call analysis
            // For now, leave type as null; CallExpr will handle it
        }

        private void AnalyzeCallExpr(CallExpr call)
        {
            // Analyze arguments first
            foreach (var arg in call.Arguments)
                AnalyzeExpr(arg);

            string[] argTypes = call.Arguments.Select(a => a.ResolvedType ?? "SystemObject").ToArray();

            // Handle builtin calls: log, warn, error, RequestSerialization, IsValid, SendCustomEventDelayedSeconds
            if (call.Callee is NameExpr nameExpr)
            {
                var sym = _currentScope.Lookup(nameExpr.Name);
                if (sym != null && sym.Kind == SymbolKind.Builtin)
                {
                    call.IsBuiltinCall = true;
                    nameExpr.ResolvedSymbol = sym;
                    switch (nameExpr.Name)
                    {
                        case "log":
                        case "warn":
                        case "error":
                            call.ResolvedType = "SystemVoid";
                            return;
                        case "RequestSerialization":
                            call.ResolvedType = "SystemVoid";
                            return;
                        case "IsValid":
                            call.ResolvedType = "SystemBoolean";
                            return;
                        case "SendCustomEventDelayedSeconds":
                            call.ResolvedType = "SystemVoid";
                            return;
                    }
                }

                // Handle constructor calls: Color(...), Vector3(...), etc.
                if (sym != null && sym.Kind == SymbolKind.StaticType)
                {
                    string udonType = sym.UdonType;
                    var sig = _catalog.ResolveStaticMethod(udonType, "ctor", argTypes);
                    if (sig != null)
                    {
                        call.ResolvedExtern = sig;
                        call.ResolvedType = udonType;
                        call.IsConstructorCall = true;
                        nameExpr.ResolvedSymbol = sym;
                        AnnotateImplicitConversions(call, sig, argTypes);
                        return;
                    }
                    // If no ctor found, fall through to try as function/undefined
                }

                // Handle function calls
                if (sym != null && sym.Kind == SymbolKind.Function)
                {
                    call.ResolvedFunctionName = nameExpr.Name;
                    call.ResolvedType = sym.UdonType;
                    nameExpr.ResolvedSymbol = sym;
                    nameExpr.ResolvedType = sym.UdonType;

                    // Record in call graph
                    if (_currentFunction != null && _callGraph.ContainsKey(_currentFunction))
                    {
                        _callGraph[_currentFunction].Add(nameExpr.Name);
                    }
                    return;
                }

                // Undefined function
                if (sym == null)
                {
                    string suggestion = _currentScope.FindClosest(nameExpr.Name);
                    _diagnostics.ReportUndefinedFunction(nameExpr.Name, call.Span, suggestion);
                    return;
                }
            }

            // Handle method calls: obj.Method(args)
            if (call.Callee is MemberExpr memberExpr)
            {
                AnalyzeExpr(memberExpr.Object);

                if (memberExpr.Object.ResolvedType == null) return;

                string ownerType = memberExpr.Object.ResolvedType;
                bool isStatic = false;

                // Check for static type access
                if (memberExpr.Object is NameExpr objName &&
                    (objName.ResolvedSymbol?.Kind == SymbolKind.StaticType ||
                     objName.ResolvedSymbol?.Kind == SymbolKind.EnumType))
                {
                    ownerType = objName.ResolvedSymbol.UdonType;
                    isStatic = true;
                }

                // Detect type references used as values: StaticType names become SystemType
                for (int i = 0; i < call.Arguments.Count; i++)
                {
                    if (call.Arguments[i] is NameExpr argName &&
                        argName.ResolvedSymbol?.Kind == SymbolKind.StaticType)
                    {
                        argTypes[i] = "SystemType";
                        argName.ResolvedType = "SystemType"; // Signal to IR lowerer
                    }
                }

                ExternSignature sig;
                if (isStatic)
                    sig = _catalog.ResolveStaticMethod(ownerType, memberExpr.MemberName, argTypes);
                else
                    sig = _catalog.ResolveMethod(ownerType, memberExpr.MemberName, argTypes);

                if (sig != null)
                {
                    call.ResolvedExtern = sig;
                    call.ResolvedType = sig.ReturnType;
                    memberExpr.ResolvedType = sig.ReturnType;

                    // Annotate implicit conversions per argument
                    AnnotateImplicitConversions(call, sig, argTypes);

                    // Override return type for GetComponent-family methods with type argument
                    if (IsGetComponentMethod(memberExpr.MemberName))
                    {
                        for (int i = 0; i < call.Arguments.Count; i++)
                        {
                            if (call.Arguments[i] is NameExpr argName &&
                                argName.ResolvedSymbol?.Kind == SymbolKind.StaticType)
                            {
                                string typeParam = argName.ResolvedSymbol.UdonType;
                                call.ResolvedType = ReturnsArray(memberExpr.MemberName)
                                    ? typeParam + "Array"
                                    : typeParam;
                                memberExpr.ResolvedType = call.ResolvedType;
                                break;
                            }
                        }
                    }

                    return;
                }

                // Better error: show available overloads
                var overloads = isStatic
                    ? _catalog.GetStaticMethodOverloads(ownerType, memberExpr.MemberName)
                    : _catalog.GetMethodOverloads(ownerType, memberExpr.MemberName);

                if (overloads.Count > 0)
                {
                    string argList = string.Join(", ", argTypes.Select(TypeSystem.ToNoriType));
                    string msg = $"No matching overload for {TypeSystem.ToNoriType(ownerType)}.{memberExpr.MemberName}" +
                                 $" with arguments ({argList})";
                    string hint = FormatOverloads(overloads);
                    _diagnostics.ReportError("E0130", msg, call.Span, hint: hint);
                }
                else
                {
                    _diagnostics.ReportError("E0130",
                        $"Method '{memberExpr.MemberName}' not available on type " +
                        $"'{TypeSystem.ToNoriType(ownerType)}'",
                        call.Span);
                }
            }
        }

        private void AnnotateImplicitConversions(CallExpr call, ExternSignature sig, string[] argTypes)
        {
            List<ImplicitConversion> conversions = null;
            for (int i = 0; i < argTypes.Length && i < sig.ParamTypes.Length; i++)
            {
                if (argTypes[i] != sig.ParamTypes[i] && sig.ParamTypes[i] != "SystemObject")
                {
                    var conv = _catalog.GetImplicitConversion(argTypes[i], sig.ParamTypes[i]);
                    if (conv != null)
                    {
                        if (conversions == null)
                        {
                            conversions = new List<ImplicitConversion>(new ImplicitConversion[argTypes.Length]);
                        }
                        conversions[i] = conv;
                    }
                }
            }
            call.ImplicitConversions = conversions;
        }

        private static string FormatOverloads(List<ExternSignature> overloads)
        {
            var lines = new List<string>();
            lines.Add("Available overloads:");
            foreach (var sig in overloads)
            {
                var paramStrs = new List<string>();
                for (int i = 0; i < sig.ParamTypes.Length; i++)
                {
                    string name = sig.Params != null && i < sig.Params.Length
                        ? sig.Params[i].Name : $"arg{i}";
                    paramStrs.Add($"{name}: {TypeSystem.ToNoriType(sig.ParamTypes[i])}");
                }
                string ret = sig.ReturnType == "SystemVoid" ? "void" : TypeSystem.ToNoriType(sig.ReturnType);
                lines.Add($"  ({string.Join(", ", paramStrs)}) -> {ret}");
            }
            return string.Join("\n", lines);
        }

        private void AnalyzeIndexExpr(IndexExpr index)
        {
            AnalyzeExpr(index.Object);
            AnalyzeExpr(index.Index);

            // Array construction: Type[size] — e.g., VRCPlayerApi[8]
            if (index.Object is NameExpr indexName &&
                indexName.ResolvedSymbol != null &&
                (indexName.ResolvedSymbol.Kind == SymbolKind.StaticType ||
                 indexName.ResolvedSymbol.Kind == SymbolKind.EnumType))
            {
                string elemType = indexName.ResolvedSymbol.UdonType;
                index.ResolvedType = elemType + "Array";
                index.IsArrayConstruction = true;
                return;
            }

            if (index.Object.ResolvedType != null &&
                index.Object.ResolvedType.EndsWith("Array"))
            {
                string elemType = index.Object.ResolvedType
                    .Substring(0, index.Object.ResolvedType.Length - 5);
                index.ResolvedType = elemType;
            }

            if (index.Index.ResolvedType != null &&
                index.Index.ResolvedType != "SystemInt32")
            {
                _diagnostics.ReportTypeMismatch("int",
                    TypeSystem.ToNoriType(index.Index.ResolvedType),
                    index.Index.Span);
            }
        }

        private void DetectRecursion()
        {
            // DFS for cycles in call graph
            var visited = new HashSet<string>();
            var inStack = new HashSet<string>();
            var path = new List<string>();

            foreach (var func in _callGraph.Keys)
            {
                if (!visited.Contains(func))
                    DfsCycleCheck(func, visited, inStack, path);
            }
        }

        private void DfsCycleCheck(string node, HashSet<string> visited,
            HashSet<string> inStack, List<string> path)
        {
            visited.Add(node);
            inStack.Add(node);
            path.Add(node);

            if (_callGraph.TryGetValue(node, out var callees))
            {
                foreach (var callee in callees)
                {
                    if (inStack.Contains(callee))
                    {
                        // Found a cycle
                        int cycleStart = path.IndexOf(callee);
                        var cyclePath = path.GetRange(cycleStart, path.Count - cycleStart);
                        cyclePath.Add(callee);
                        string chain = string.Join(" -> ", cyclePath);

                        // Find the span of the function declaration
                        var funcDecl = _module.Declarations.OfType<FunctionDecl>()
                            .FirstOrDefault(f => f.Name == callee);
                        var span = funcDecl?.Span ?? SourceSpan.None;

                        _diagnostics.ReportRecursionDetected(chain, span);
                        return;
                    }

                    if (!visited.Contains(callee) && _callGraph.ContainsKey(callee))
                        DfsCycleCheck(callee, visited, inStack, path);
                }
            }

            inStack.Remove(node);
            path.RemoveAt(path.Count - 1);
        }

        private string ResolveTypeName(string typeName, bool isArray, SourceSpan span)
        {
            if (typeName == null) return null;

            string fullType = isArray ? typeName + "[]" : typeName;
            string udonType = TypeSystem.ResolveType(fullType);

            if (udonType == null)
            {
                // Check if it looks like a generic type
                if (typeName.Contains("<") || typeName.Contains(">"))
                {
                    _diagnostics.ReportGenericTypeUsed(typeName, span);
                }
                else
                {
                    _diagnostics.ReportError("E0040",
                        $"Unknown type '{fullType}'", span);
                }
            }

            return udonType;
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

        private static string TokenKindToString(TokenKind kind)
        {
            switch (kind)
            {
                case TokenKind.Plus: return "+";
                case TokenKind.Minus: return "-";
                case TokenKind.Star: return "*";
                case TokenKind.Slash: return "/";
                case TokenKind.Percent: return "%";
                case TokenKind.EqualsEquals: return "==";
                case TokenKind.BangEquals: return "!=";
                case TokenKind.Less: return "<";
                case TokenKind.LessEquals: return "<=";
                case TokenKind.Greater: return ">";
                case TokenKind.GreaterEquals: return ">=";
                case TokenKind.And: return "&&";
                case TokenKind.Or: return "||";
                case TokenKind.Bang: return "!";
                default: return kind.ToString();
            }
        }

        public static string GetUdonEventName(string noriEventName)
        {
            return EventNameMap.TryGetValue(noriEventName, out var udonName)
                ? udonName : "_" + noriEventName;
        }

        private static bool IsGetComponentMethod(string name) =>
            name == "GetComponent" || name == "GetComponents" ||
            name == "GetComponentInChildren" || name == "GetComponentsInChildren" ||
            name == "GetComponentInParent" || name == "GetComponentsInParent";

        private static bool ReturnsArray(string name) =>
            name == "GetComponents" || name == "GetComponentsInChildren" ||
            name == "GetComponentsInParent";
    }
}
