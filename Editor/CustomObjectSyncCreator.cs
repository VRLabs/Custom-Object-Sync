using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.VersionControl;
using UnityEngine;
using UnityEngine.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using static VRC.SDKBase.VRC_AvatarParameterDriver;
using static VRLabs.CustomObjectSyncCreator.ControllerGenerationMethods;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace VRLabs.CustomObjectSyncCreator
{
	public class CustomObjectSyncCreator
	{
		private static CustomObjectSyncCreator instance;
		public static CustomObjectSyncCreator Instance
		{
			get
			{
				if (instance == null)
				{
					instance = new CustomObjectSyncCreator();
				}
				return instance;
			}
		}

		public GameObject syncObject;
		public AnimatorController resourceController;
		public GameObject resourcePrefab;
		public int bitCount = 16;
		public int maxRadius = 7;
		public int positionPrecision = 6;
		public int rotationPrecision = 8;
		public bool rotationEnabled = true;
		public bool centeredOnAvatar = false;
		public bool addDampeningConstraint = false;
		public float dampingConstraintValue = 0.1f;

		private string[] names = new[] { "X", "Y", "Z" };
		public void Generate()
		{
			AnimatorController mergedController = null;

			VRCAvatarDescriptor descriptor = syncObject.GetComponentInParent<VRCAvatarDescriptor>();
			
			if (descriptor == null) return;

			#region Resource Setup

			descriptor.customizeAnimationLayers = true;
			RuntimeAnimatorController runtimeController = descriptor.baseAnimationLayers
				.Where(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX).Select(x => x.animatorController)
				.FirstOrDefault();
			AnimatorController mergeController = runtimeController == null ? null : (AnimatorController) runtimeController;

			VRCExpressionParameters parameterObject = descriptor.expressionParameters;
			VRCExpressionsMenu menuObject = descriptor.expressionsMenu;
			
			if (mergeController == null)
			{
				Directory.CreateDirectory(AnimatorUtils.STANDARD_NEW_ANIMATOR_FOLDER);
				string uniquePath = AssetDatabase.GenerateUniqueAssetPath(AnimatorUtils.STANDARD_NEW_ANIMATOR_FOLDER + "CustomObjectCreator.controller");
				AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(resourceController), uniquePath);
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
				mergedController = AssetDatabase.LoadAssetAtPath<AnimatorController>(uniquePath);
			}
			else
			{
				mergedController = AnimatorUtils.MergeControllers(mergeController, resourceController,
					new Dictionary<string, string>(), true);
			}
			
			if (mergedController == null)
			{
				Debug.LogError("Creation of Controller object failed. Please report this to jellejurre on the VRLabs discord at discord.vrlabs.dev");
				return;
			}


			if (parameterObject == null)
			{
				Directory.CreateDirectory(AnimatorUtils.STANDARD_NEW_PARAMASSET_FOLDER);
				string uniquePath = AssetDatabase.GenerateUniqueAssetPath(AnimatorUtils.STANDARD_NEW_PARAMASSET_FOLDER + "Parameters.asset");
				parameterObject = ScriptableObject.CreateInstance<VRCExpressionParameters>();
				parameterObject.parameters = Array.Empty<VRCExpressionParameters.Parameter>();
				AssetDatabase.CreateAsset(parameterObject, uniquePath);
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
				parameterObject = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(uniquePath);
			}

			if (parameterObject == null)
			{
				Debug.LogError("Creation of Parameter object failed. Please report this to jellejurre on the VRLabs discord at discord.vrlabs.dev");
				return;
			}
			
			if (menuObject == null)
			{
				Directory.CreateDirectory(AnimatorUtils.STANDARD_NEW_MENUASSET_FOLDER);
				string uniquePath = AssetDatabase.GenerateUniqueAssetPath(AnimatorUtils.STANDARD_NEW_MENUASSET_FOLDER + "Menu.asset");
				menuObject = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
				menuObject.controls = new List<VRCExpressionsMenu.Control>();
				AssetDatabase.CreateAsset(menuObject, uniquePath);
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
				menuObject = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(uniquePath);
			}
			
			if (menuObject == null)
			{
				Debug.LogError("Creation of Menu object failed. Please report this to jellejurre on the VRLabs discord at discord.vrlabs.dev");
				return;
			}
			
			AnimationClip buffer = GenerateClip("Buffer");
			AddCurve(buffer, "THIS PATH DOES NOT EXIST", typeof(Object), "NOPE", AnimationCurve.Constant(0, 1/60f, 0));

			#endregion
			
			#region Bit Conversion
			AnimatorStateMachine positionMachine = GenerateStateMachine("CustomObjectSync/Position Bit Convert", new Vector3(-80, 0, 0), new Vector3(-80, 200 , 0), new Vector3(-80, 100, 0));
			AnimatorControllerLayer positionLayer = GenerateLayer("CustomObjectSync/Position Bit Convert", positionMachine);
			ChildAnimatorState initialState = GenerateChildState(new Vector3(-100, 400, 0), GenerateState("Initial", motion: buffer));


			int positionBits = maxRadius + positionPrecision;
			int rotationBits = rotationEnabled ? rotationPrecision : 0;
			SetupAnimationControllerCopy("Position", buffer, initialState, positionMachine, positionBits,  true, positionBits > rotationBits);
			SetupAnimationControllerCopy("Position", buffer, initialState, positionMachine, positionBits,  false, positionBits > rotationBits);
			positionMachine.states = new[] { initialState }.Concat(positionMachine.states).ToArray();
			positionMachine.defaultState = initialState.state;

			AnimatorControllerLayer rotationLayer = null;
			if (rotationEnabled)
			{
				AnimatorStateMachine rotationMachine = GenerateStateMachine("CustomObjectSync/Rotation Bit Convert", new Vector3(-80, 0, 0), new Vector3(-80, 200 , 0), new Vector3(-80, 100, 0));
				rotationLayer = GenerateLayer("CustomObjectSync/Rotation Bit Convert", rotationMachine);
				ChildAnimatorState initialRotationState = GenerateChildState(new Vector3(-100, 400, 0), GenerateState("Initial", motion: buffer));

				SetupAnimationControllerCopy("Rotation", buffer, initialRotationState, rotationMachine, rotationBits,  true, positionBits <= rotationBits);	
				SetupAnimationControllerCopy("Rotation", buffer, initialRotationState, rotationMachine, rotationBits,  false, positionBits <= rotationBits);	
				rotationMachine.states = new[] { initialRotationState }.Concat(rotationMachine.states).ToArray();
				rotationMachine.defaultState = initialRotationState.state;
			}

			#endregion

			#region Bit Parameters
			List<AnimatorControllerParameter> parameters = new List<AnimatorControllerParameter>();
			for (int p = 0; p < names.Length; p++)
			{
				parameters.Add(GenerateFloatParameter($"CustomObjectSync/Temp/Position{names[p]}"));
			}
			
			for (int p = 0; p < names.Length; p++)
			{
				for (int b = 0; b < positionBits; b++)
				{
					parameters.Add(GenerateBoolParameter($"CustomObjectSync/Bits/Position{names[p]}{b}"));
					parameters.Add(GenerateBoolParameter($"CustomObjectSync/Bits/Copy/Position{names[p]}{b}"));
				}				
			}

			if (rotationEnabled)
			{
				for (int p = 0; p < names.Length; p++)
				{
					parameters.Add(GenerateFloatParameter($"CustomObjectSync/Temp/Rotation{names[p]}"));
				}
			
				for (int p = 0; p < names.Length; p++)
				{
					for (int b = 0; b < rotationBits; b++)
					{
						parameters.Add(GenerateBoolParameter($"CustomObjectSync/Bits/Rotation{names[p]}{b}"));
						parameters.Add(GenerateBoolParameter($"CustomObjectSync/Bits/Copy/Rotation{names[p]}{b}"));
					}				
				}
			}
			mergedController.parameters = mergedController.parameters.Concat(parameters).ToArray();
			#endregion

			mergedController.layers = mergedController.layers.Append(positionLayer).ToArray();
			if (rotationEnabled)
			{
				mergedController.layers = mergedController.layers.Append(rotationLayer).ToArray();
			}
			
			// Sync Steps
			AnimatorStateMachine syncMachine = GenerateStateMachine("CustomObjectSync/Sync", new Vector3(-80, 0, 0), new Vector3(-80, 200 , 0), new Vector3(-80, 100, 0));
			AnimatorControllerLayer syncLayer = GenerateLayer("CustomObjectSync/Sync", syncMachine);
			int syncSteps = Mathf.CeilToInt(GetMaxBitCount() / (float)bitCount);
			int syncStepBools = Mathf.CeilToInt(Mathf.Log(syncSteps, 2));
			bool[][] syncStepPerms = GeneratePermutations(syncStepBools);
			
			AnimationClip bufferWaitInit = GenerateClip($"BufferWait{(int)(syncSteps*1.5f)}");
			AddCurve(bufferWaitInit, "Custom Object Sync/Measure", typeof(PositionConstraint), "m_Enabled", AnimationCurve.Constant(0, ((Math.Max(positionBits, rotationBits)*1.5f))/60f, 0));
			AddCurve(bufferWaitInit, "Custom Object Sync/Set", typeof(PositionConstraint), "m_Enabled", AnimationCurve.Constant(0, ((Math.Max(positionBits, rotationBits)*1.5f))/60f, 0));

			AnimationClip bufferWaitSync = GenerateClip($"BufferWaitSync");
			AddCurve(bufferWaitSync, "Custom Object Sync/Measure", typeof(PositionConstraint), "m_Enabled", AnimationCurve.Constant(0, 12/60f, 0));
			AddCurve(bufferWaitSync, "Custom Object Sync/Set", typeof(PositionConstraint), "m_Enabled", AnimationCurve.Constant(0, 12/60f, 0));
			
			AnimationClip enableWorldConstraint = GenerateClip($"EnableWorldConstraint");
			AddCurve(enableWorldConstraint, "Custom Object Sync/Measure", typeof(PositionConstraint), "m_Enabled", AnimationCurve.Constant(0, 12/60f, 1));
			AddCurve(enableWorldConstraint, "Custom Object Sync/Set", typeof(PositionConstraint), "m_Enabled", AnimationCurve.Constant(0, 12/60f, 1));
			
			#region SyncStates
			ChildAnimatorState initState = GenerateChildState(new Vector3(-100, -150, 0), GenerateState("SyncInit", motion: enableWorldConstraint));
			string[] syncParameterNames = names
				.SelectMany(n => Enumerable.Range(0, positionBits).Select(i => $"CustomObjectSync/Bits/Copy/Position{n}{i}"))
				.Concat(names.SelectMany(n =>
					Enumerable.Range(0, rotationBits).Select(i => $"CustomObjectSync/Bits/Copy/Rotation{n}{i}"))).ToArray();
			string[] localSyncParameterNames = names
				.SelectMany(n => Enumerable.Range(0, positionBits).Select(i => $"CustomObjectSync/Bits/Position{n}{i}"))
				.Concat(names.SelectMany(n =>
					Enumerable.Range(0, rotationBits).Select(i => $"CustomObjectSync/Bits/Rotation{n}{i}"))).ToArray();

			int stepToStartSync = Mathf.CeilToInt(Math.Max(positionBits, rotationBits) / 12f);
			bool shouldDelayFirst = (stepToStartSync > syncSteps);
			
			
			List<ChildAnimatorState> localStates = new List<ChildAnimatorState>();
			for (int i = 0; i < syncSteps; i++)
			{
				localStates.Add(GenerateChildState(new Vector3(-500,-(syncSteps) * 25 + ((i + 1) * 50), 0), GenerateState($"SyncLocal{i}", motion: bufferWaitSync)));
				if (shouldDelayFirst)
				{
					localStates.Last().state.motion = bufferWaitInit;
				}
				
				if (i == syncSteps - 1)
				{
					// When we begin sending copy out values so we have them ready to send
					localStates.Last().state.behaviours = localStates.Last().state.behaviours
						.Append(GenerateParameterDriver(Enumerable.Range(0, syncParameterNames.Length).Select(x => GenerateParameter(ChangeType.Copy, localSyncParameterNames[x], syncParameterNames[x])).ToArray())).ToArray(); 
				}
				if (syncSteps - i == stepToStartSync)
				{
					localStates.Last().state.behaviours = localStates.Last().state.behaviours
						.Append(GenerateParameterDriver(new[]
							{ GenerateParameter(ChangeType.Set, name: "CustomObjectSync/StartRead", value: 1) }))
						.ToArray();
				}

				
				localStates.Add(GenerateChildState(new Vector3(-800, -(syncSteps) * 25 + ((i + 1) * 50), 0), GenerateState($"SyncLocal{i}Buffer", motion: bufferWaitSync)));
				localStates.Last().state.behaviours = new[]
				{
					GenerateParameterDriver(Enumerable.Range(0, syncStepBools).Select(x => GenerateParameter(ChangeType.Set, name: $"CustomObjectSync/Sync/Step{x}", value: syncStepPerms[(i + 1) % (syncSteps)][x] ? 1 : 0)).ToArray()),
					GenerateParameterDriver(
						Enumerable
							.Range(((i + 1) % syncSteps) * bitCount, Math.Min((((i + 1) % syncSteps) + 1) * bitCount, GetMaxBitCount()) - ((((i + 1) % syncSteps)) * bitCount))
							.Select(x => GenerateParameter(ChangeType.Copy, source: syncParameterNames[x],
								name: $"CustomObjectSync/Sync/Data{x % bitCount}"))
							.ToArray())
				};
			}
			
			List<ChildAnimatorState> remoteStates = new List<ChildAnimatorState>();
			for (int i = 0; i < syncSteps; i++)
			{
				remoteStates.Add(GenerateChildState(new Vector3(300, -(syncSteps) * 25 + ((i + 1) * 50), 0), GenerateState($"SyncRemote{i}", motion: bufferWaitSync)));
				remoteStates.Last().state.behaviours = new[]
				{
					GenerateParameterDriver(
						Enumerable
							.Range(i * bitCount, Math.Min((i + 1) * bitCount, GetMaxBitCount()) - (i * bitCount))
							.Select(x => GenerateParameter(ChangeType.Copy, source: $"CustomObjectSync/Sync/Data{x % bitCount}", name: syncParameterNames[x]))
							.ToArray())
				};
			}

			remoteStates.Last().state.behaviours =
				remoteStates.Last().state.behaviours.Append(GenerateParameterDriver(new [] {GenerateParameter(ChangeType.Set, name: "CustomObjectSync/StartWrite", value: 1)})).ToArray();
			remoteStates.Last().state.behaviours = remoteStates.Last().state.behaviours
				.Append(GenerateParameterDriver(Enumerable.Range(0, syncParameterNames.Length).Select(x => GenerateParameter(ChangeType.Copy, syncParameterNames[x], localSyncParameterNames[x])).ToArray())).ToArray();

			#endregion
			
			#region SyncTransitions
			
			List<AnimatorStateTransition> syncAnyStateTransitions = new List<AnimatorStateTransition>();
			
			syncAnyStateTransitions.Add(GenerateTransition("", conditions: new [] {GenerateCondition(AnimatorConditionMode.IfNot, "CustomObjectSync/Enabled", 0)}, destinationState: initState.state));
			
			for (int i = 0; i < localStates.Count/2; i++)
			{
				syncAnyStateTransitions.Add(GenerateTransition("",  conditions: Enumerable.Range(0, syncStepBools)
					.Select(x => GenerateCondition(syncStepPerms[(i) % (syncSteps)][x] ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, $"CustomObjectSync/Sync/Step{x}", 0))
					.Append(GenerateCondition(AnimatorConditionMode.If, "IsLocal", 0))
					.Append(GenerateCondition(AnimatorConditionMode.If, "CustomObjectSync/Enabled", 0)).ToArray(), destinationState: localStates[i * 2].state));

				localStates[i * 2].state.transitions = new[]
				{
					GenerateTransition("", destinationState: localStates[(i*2)+1].state, hasExitTime: true, exitTime: 1)
				};
			}
			
			for (int i = 0; i < remoteStates.Count; i++)
			{
				syncAnyStateTransitions.Add(GenerateTransition("", canTransitionToSelf: true, conditions: Enumerable.Range(0, syncStepBools)
					.Select(x => GenerateCondition(syncStepPerms[(i) % (syncSteps)][x] ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, $"CustomObjectSync/Sync/Step{x}", 0))
					.Append(GenerateCondition(AnimatorConditionMode.IfNot, "IsLocal", 0))
					.Append(GenerateCondition(AnimatorConditionMode.If, "CustomObjectSync/Enabled", 0)).ToArray(), destinationState: remoteStates[i].state));
			}
			syncMachine.anyStateTransitions = syncAnyStateTransitions.ToArray();
			#endregion
			
			#region SyncParameters
			List<AnimatorControllerParameter> syncParameters = new List<AnimatorControllerParameter>();
			for (int i = 0; i < bitCount; i++)
			{
				syncParameters.Add(GenerateBoolParameter( $"CustomObjectSync/Sync/Data{i}", false));
			}
			for (int i = 0; i < syncStepBools; i++)
			{
				syncParameters.Add(GenerateBoolParameter( $"CustomObjectSync/Sync/Step{i}", syncStepPerms[syncSteps-1][i]));
			}
			mergedController.parameters = mergedController.parameters.Concat(syncParameters).ToArray();
			#endregion
			
			syncMachine.states = localStates.Concat(remoteStates).Concat(new [] {initState }).ToArray();
			syncMachine.defaultState = initState.state;
			// 0 (read)
			// 1 - syncSteps (Send)
			// syncSteps (remotely, write)
			
			
			#region VRCParameters
			List<VRCExpressionParameters.Parameter> parameterList = new List<VRCExpressionParameters.Parameter>();
			parameterList.Add(GenerateVRCParameter("CustomObjectSync/Enabled", VRCExpressionParameters.ValueType.Bool));
			for (int b = 0; b < bitCount; b++)
			{
				parameterList.Add(GenerateVRCParameter($"CustomObjectSync/Sync/Data{b}", VRCExpressionParameters.ValueType.Bool));
			}

			for (int b = 0; b < syncStepBools; b++)
			{
				parameterList.Add(GenerateVRCParameter($"CustomObjectSync/Sync/Step{b}", VRCExpressionParameters.ValueType.Bool, defaultValue:  syncStepPerms[syncSteps-1][b] ? 1 : 0));
			}

			parameterObject.parameters = parameterObject.parameters.Concat(parameterList).ToArray();
			
			menuObject.controls.Add(new VRCExpressionsMenu.Control()
			{
				name = "Enable Custom Object Sync",
				style = VRCExpressionsMenu.Control.Style.Style1,
				type = VRCExpressionsMenu.Control.ControlType.Toggle,
				parameter = new VRCExpressionsMenu.Control.Parameter()
				{
					name = "CustomObjectSync/Enabled"
				}
			});

			descriptor.expressionsMenu = menuObject;
			#endregion

			#region Installation

			GameObject rootObject = descriptor.gameObject;
			GameObject syncSystem = GameObject.Instantiate(resourcePrefab, rootObject.transform);
			syncSystem.name = syncSystem.name.Replace("(Clone)", "");
			if (!rotationEnabled)
			{
				GameObject.DestroyImmediate(syncSystem.transform.Find("Measure/Rotation").gameObject);
				GameObject.DestroyImmediate(syncSystem.transform.Find("Set/Result").GetComponent<RotationConstraint>());
			}

			foreach (string s in names)
			{
				Transform sender = syncSystem.transform.Find($"Measure/Position/Sender{s}");
				float radius = MathF.Pow(2, maxRadius);
				PositionConstraint sendConstraint = sender.GetComponent<PositionConstraint>();
				sendConstraint.translationAtRest = new Vector3(0, 0, 0);
				ConstraintSource source0 = sendConstraint.GetSource(0);
				source0.weight = 1 - (3f / (radius));	
				sendConstraint.SetSource(0, source0);
				ConstraintSource source1 = sendConstraint.GetSource(1);
				source1.weight = 3f / (radius);
				sendConstraint.SetSource(1, source1);
			}

			Transform targetObject = syncSystem.transform.Find("Target");
			targetObject.parent = syncObject.transform.parent;
			targetObject.name = $"{syncObject.name}{" Target"}";
			targetObject.localPosition = syncObject.transform.localPosition;

			string oldPath = AnimationUtility.CalculateTransformPath(syncObject.transform, descriptor.transform);
			syncObject.transform.parent = syncSystem.transform;
			string newPath = AnimationUtility.CalculateTransformPath(syncObject.transform, descriptor.transform);
			
			AnimationClip[] allClips = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
				.Where(x => x.animatorController != null).SelectMany(x => x.animatorController.animationClips)
				.ToArray();
			AnimatorUtils.RenameClipPaths(allClips, false, oldPath, newPath);

			ParentConstraint targetConstraint = syncObject.AddComponent<ParentConstraint>();
			targetConstraint.AddSource(new ConstraintSource()
			{
				sourceTransform = targetObject, weight = 1
			});
			targetConstraint.AddSource(new ConstraintSource()
			{
				sourceTransform = syncSystem.transform.Find("Set/Result"), weight = 0f
			});
			if (addDampeningConstraint)
			{
				targetConstraint.AddSource(new ConstraintSource()
				{
					sourceTransform = syncObject.transform, weight = 0
				});
			}
			targetConstraint.locked = true;
			targetConstraint.constraintActive = true;

			if (centeredOnAvatar)
			{
				Transform setTransform = syncSystem.transform.Find("Set");
				Transform measureTransform = syncSystem.transform.Find("Measure");
				PositionConstraint setConstraint = setTransform.gameObject.AddComponent<PositionConstraint>();
				PositionConstraint measureConstraint = measureTransform.gameObject.AddComponent<PositionConstraint>();
				setConstraint.AddSource(new ConstraintSource()
				{
					sourceTransform = descriptor.transform, weight = 1f
				});
				measureConstraint.AddSource(new ConstraintSource()
				{
					sourceTransform = descriptor.transform, weight = 1f
				});
				setConstraint.translationAtRest = Vector3.zero;
				setConstraint.translationOffset = Vector3.zero;
				setConstraint.locked = true;
				setConstraint.constraintActive = true;
				measureConstraint.translationAtRest = Vector3.zero;
				measureConstraint.translationOffset = Vector3.zero;
				measureConstraint.locked = true;
				measureConstraint.constraintActive = true;
			}
			
			VRCAvatarDescriptor.CustomAnimLayer[] layers = descriptor.baseAnimationLayers;
			int fxLayerIndex = layers.ToList().FindIndex(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX);
			VRCAvatarDescriptor.CustomAnimLayer fxLayer = layers[fxLayerIndex];
			fxLayer.isDefault = false;
			fxLayer.animatorController = mergedController;
			layers[fxLayerIndex] = fxLayer;
			descriptor.baseAnimationLayers = layers;

			descriptor.customExpressions = true;
			descriptor.expressionParameters = parameterObject;
			
			Selection.activeObject = descriptor.gameObject;
			#endregion

			#region Result Displaying
			AnimatorControllerLayer layer = mergedController.layers.FirstOrDefault(x => x.name == "CustomObjectSync/Parameter Initialization");
			if (layer != null)
			{
				string targetString = AnimationUtility.CalculateTransformPath(syncObject.transform, descriptor.transform);
				AnimatorState remoteOffState = layer.stateMachine.states.First(x => x.state.name == "Remote Off").state;
				AnimationClip remoteParentConstraintOff = GenerateClip("remoteParentConstraintDisabled");
				AddCurve(remoteParentConstraintOff, targetString, typeof(ParentConstraint), "m_Sources.Array.data[0].weight", AnimationCurve.Constant(0, 1/60f, 1));
				AddCurve(remoteParentConstraintOff, targetString, typeof(ParentConstraint), "m_Sources.Array.data[1].weight", AnimationCurve.Constant(0, 1/60f, 0));
				AddCurve(remoteParentConstraintOff, targetString, typeof(ParentConstraint), "m_Sources.Array.data[2].weight", AnimationCurve.Constant(0, 1/60f, 0));
				remoteOffState.motion = remoteParentConstraintOff;
				
				ChildAnimatorState[] localStatesInit = layer.stateMachine.states.Where(x => x.state.name.Contains("X")).ToArray();
				AnimationClip enableMeasure = GenerateClip("localMeasureEnabled");
				AddCurve(enableMeasure, "Custom Object Sync/Measure", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1/60f, 1));
				foreach (var childAnimatorState in localStatesInit)
				{
					childAnimatorState.state.motion = enableMeasure;
				}

				AnimatorState remoteOnState = layer.stateMachine.states.First(x => x.state.name == "Remote On").state;
				BlendTree remoteTree = GenerateBlendTree("RemoteTree", BlendTreeType.Direct);
				AnimationClip remoteParentConstraintOn = GenerateClip("remoteParentConstraintEnabled");
				AddCurve(remoteParentConstraintOn, targetString, typeof(ParentConstraint), "m_Sources.Array.data[0].weight", AnimationCurve.Constant(0, 1/60f, 0));
				if (addDampeningConstraint)
				{
					AddCurve(remoteParentConstraintOn, targetString, typeof(ParentConstraint), "m_Sources.Array.data[1].weight", AnimationCurve.Constant(0, 1/60f, dampingConstraintValue));
					AddCurve(remoteParentConstraintOn, targetString, typeof(ParentConstraint), "m_Sources.Array.data[2].weight", AnimationCurve.Constant(0, 1/60f, 1));
				}
				else
				{
					AddCurve(remoteParentConstraintOn, targetString, typeof(ParentConstraint), "m_Sources.Array.data[1].weight", AnimationCurve.Constant(0, 1/60f, 1));
				}


				AnimationClip rotationXMin = GenerateClip("RotationXMin");
				AddCurve(rotationXMin, "Custom Object Sync/Set/Result", typeof(RotationConstraint), "m_RotationOffset.x", AnimationCurve.Constant(0, 1/60f, -180));
				AnimationClip rotationXMax = GenerateClip("RotationXMax");
				AddCurve(rotationXMax, "Custom Object Sync/Set/Result", typeof(RotationConstraint), "m_RotationOffset.x", AnimationCurve.Constant(0, 1/60f, 180));
				BlendTree rotationXTree = GenerateBlendTree("RotationX", BlendTreeType.Simple1D,
					blendParameter: "CustomObjectSync/RotationX");
				rotationXTree.children = new ChildMotion[]
				{
					GenerateChildMotion(motion: rotationXMin),
					GenerateChildMotion(motion: rotationXMax)
				};
                
				AnimationClip rotationYMin = GenerateClip("RotationYMin");
				AddCurve(rotationYMin, "Custom Object Sync/Set/Result", typeof(RotationConstraint), "m_RotationOffset.y", AnimationCurve.Constant(0, 1/60f, -180));
				AnimationClip rotationYMax = GenerateClip("RotationYMax");
				AddCurve(rotationYMax, "Custom Object Sync/Set/Result", typeof(RotationConstraint), "m_RotationOffset.y", AnimationCurve.Constant(0, 1/60f, 180));
				BlendTree rotationYTree = GenerateBlendTree("RotationY", BlendTreeType.Simple1D,
					blendParameter: "CustomObjectSync/RotationY");
				rotationYTree.children = new ChildMotion[]
				{
					GenerateChildMotion(motion: rotationYMin),
					GenerateChildMotion(motion: rotationYMax)
				};
				
				AnimationClip rotationZMin = GenerateClip("RotationZMin");
				AddCurve(rotationZMin, "Custom Object Sync/Set/Result", typeof(RotationConstraint), "m_RotationOffset.z", AnimationCurve.Constant(0, 1/60f, -180));
				AnimationClip rotationZMax = GenerateClip("RotationZMax");
				AddCurve(rotationZMax, "Custom Object Sync/Set/Result", typeof(RotationConstraint), "m_RotationOffset.z", AnimationCurve.Constant(0, 1/60f, 180));
				BlendTree rotationZTree = GenerateBlendTree("RotationZ", BlendTreeType.Simple1D,
					blendParameter: "CustomObjectSync/RotationZ");
				rotationZTree.children = new ChildMotion[]
				{
					GenerateChildMotion(motion: rotationZMin),
					GenerateChildMotion(motion: rotationZMax)
				};
				
				
				AnimationClip translationXMin = GenerateClip("TranslationXMin");
				AddCurve(translationXMin, "Custom Object Sync/Set/Result", typeof(PositionConstraint), "m_TranslationOffset.x", AnimationCurve.Constant(0, 1/60f, -MathF.Pow(2, maxRadius)));
				AnimationClip translationXMax = GenerateClip("TranslationXMax");
				AddCurve(translationXMax, "Custom Object Sync/Set/Result", typeof(PositionConstraint), "m_TranslationOffset.x", AnimationCurve.Constant(0, 1/60f, MathF.Pow(2, maxRadius)));
				BlendTree translationXTree = GenerateBlendTree("TranslationX", BlendTreeType.Simple1D,
					blendParameter: "CustomObjectSync/PositionX");
				translationXTree.children = new ChildMotion[]
				{
					GenerateChildMotion(motion: translationXMin),
					GenerateChildMotion(motion: translationXMax)
				};
				
				AnimationClip translationYMin = GenerateClip("TranslationYMin");
				AddCurve(translationYMin, "Custom Object Sync/Set/Result", typeof(PositionConstraint), "m_TranslationOffset.y", AnimationCurve.Constant(0, 1/60f, -MathF.Pow(2, maxRadius)));
				AnimationClip translationYMax = GenerateClip("TranslationYMax");
				AddCurve(translationYMax, "Custom Object Sync/Set/Result", typeof(PositionConstraint), "m_TranslationOffset.y", AnimationCurve.Constant(0, 1/60f, MathF.Pow(2, maxRadius)));
				BlendTree translationYTree = GenerateBlendTree("TranslationY", BlendTreeType.Simple1D,
					blendParameter: "CustomObjectSync/PositionY");
				translationYTree.children = new ChildMotion[]
				{
					GenerateChildMotion(motion: translationYMin),
					GenerateChildMotion(motion: translationYMax)
				};
				
				AnimationClip translationZMin = GenerateClip("TranslationZMin");
				AddCurve(translationZMin, "Custom Object Sync/Set/Result", typeof(PositionConstraint), "m_TranslationOffset.z", AnimationCurve.Constant(0, 1/60f, -MathF.Pow(2, maxRadius)));
				AnimationClip translationZMax = GenerateClip("TranslationZMax");
				AddCurve(translationZMax, "Custom Object Sync/Set/Result", typeof(PositionConstraint), "m_TranslationOffset.z", AnimationCurve.Constant(0, 1/60f, MathF.Pow(2, maxRadius)));
				BlendTree translationZTree = GenerateBlendTree("TranslationZ", BlendTreeType.Simple1D,
					blendParameter: "CustomObjectSync/PositionZ");
				translationZTree.children = new ChildMotion[]
				{
					GenerateChildMotion(motion: translationZMin),
					GenerateChildMotion(motion: translationZMax)
				};
				
				
				remoteTree.children = new[]
				{
					GenerateChildMotion(rotationXTree, directBlendParameter: "CustomObjectSync/One"),
					GenerateChildMotion(rotationYTree, directBlendParameter: "CustomObjectSync/One"),
					GenerateChildMotion(rotationZTree, directBlendParameter: "CustomObjectSync/One"),
					GenerateChildMotion(translationXTree, directBlendParameter: "CustomObjectSync/One"),
					GenerateChildMotion(translationYTree, directBlendParameter: "CustomObjectSync/One"),
					GenerateChildMotion(translationZTree, directBlendParameter: "CustomObjectSync/One"),
					GenerateChildMotion(remoteParentConstraintOn, directBlendParameter: "CustomObjectSync/One")
				};
				
				remoteOnState.motion = remoteTree;

			}
			#endregion
			
			mergedController.layers = mergedController.layers.Append(syncLayer).ToArray();
			foreach (AnimatorState state in mergedController.layers.Where(x => x.name.Contains("CustomObjectSync")).SelectMany(x => x.stateMachine.states).Select(x => x.state))
			{
				if (state.motion is null)
				{
					state.motion = buffer;
				}
			}
			SerializeController(mergedController);
			
			Directory.CreateDirectory(AnimatorUtils.STANDARD_NEW_ANIMATION_FOLDER);
			foreach (var clip in mergedController.animationClips)
			{
				if (!AssetDatabase.IsMainAsset(clip)){
					if (String.IsNullOrEmpty(clip.name))
					{
						clip.name = "Anim";
					}
					var uniqueFileName = AssetDatabase.GenerateUniqueAssetPath($"{AnimatorUtils.STANDARD_NEW_ANIMATION_FOLDER}{clip.name}.anim");
					AssetDatabase.CreateAsset(clip, uniqueFileName);
				}
			}

			EditorUtility.DisplayDialog("Success!", "Custom Object Sync has been successfully installed", "Ok");
		}

		private void SetupAnimationControllerCopy(string name, AnimationClip buffer, ChildAnimatorState initialState,
			AnimatorStateMachine machine, int bits, bool read, bool copyBoth)
		{

			bool[][] permutations = GeneratePermutations(3);

			int multiplier = read ? -1 : 1;
			string mode = read ? "Read" : "Write";
			
			#region states
			ChildAnimatorState startState = GenerateChildState(new Vector3((-100) + multiplier * 300, 200, 0), GenerateState($"Start{mode}", motion: buffer));
			ChildAnimatorState endState = GenerateChildState(new Vector3((-100) + multiplier * 300 * (bits + 2) , 200, 0), GenerateState($"End{mode}", motion: buffer));
			startState.state.behaviours = new[]
			{
				read 
					? GenerateParameterDriver(names.Select(x => GenerateParameter(ChangeType.Copy, source: $"CustomObjectSync/{name}{x}", name: $"CustomObjectSync/Temp/{name}{x}")).ToArray())
					: GenerateParameterDriver(names.Select(x => GenerateParameter(ChangeType.Set, name: $"CustomObjectSync/Temp/{name}{x}", value: 0)).ToArray())
			};
			
			endState.state.behaviours = new[]
			{
				GenerateParameterDriver(new[] { GenerateParameter(ChangeType.Set, name: $"CustomObjectSync/Start{mode}", value: 0) })
			};
			
			if (!read && copyBoth)
			{
				endState.state.behaviours = endState.state.behaviours.Append(GenerateParameterDriver(names.Select(x => GenerateParameter(ChangeType.Copy, source: $"CustomObjectSync/Temp/Position{x}", name: $"CustomObjectSync/Position{x}")).ToArray())).ToArray();
				endState.state.behaviours = endState.state.behaviours.Append(GenerateParameterDriver(names.Select(x => GenerateParameter(ChangeType.Copy, source: $"CustomObjectSync/Temp/Rotation{x}", name: $"CustomObjectSync/Rotation{x}")).ToArray())).ToArray();
			}
			

			List<List<ChildAnimatorState>> bitStates = new List<List<ChildAnimatorState>>();
			for (int b = 0; b < bits; b++)
			{
				List<ChildAnimatorState> states = new List<ChildAnimatorState>();
				bitStates.Add(states);
				for (int s = 0; s < 8; s++)
				{
					bool[] perm = permutations[s];
					string permString = perm.Aggregate("", (s1, b1) => s1 += (b1 ? "1" : "0"));
					AnimatorState state = GenerateState($"Bit{mode}-{b}-{permString}", motion: buffer);

					List<Parameter> parameterDriverParams = new List<Parameter>();
					for (int p = 0; p < perm.Length; p++)
					{
						if (read)
						{
							if (perm[p])
							{
								parameterDriverParams.Add(GenerateParameter(ChangeType.Set, name: $"CustomObjectSync/Bits/{name}{names[p]}{b}", value: 1));
								parameterDriverParams.Add(GenerateParameter(ChangeType.Add, name: $"CustomObjectSync/Temp/{name}{names[p]}", value: -1 * Mathf.Pow(0.5f, b + 1)));
							}
							else
							{
								parameterDriverParams.Add(GenerateParameter(ChangeType.Set, name: $"CustomObjectSync/Bits/{name}{names[p]}{b}", value: 0));
							}
						}
						else
						{
							if (perm[p])
							{
								parameterDriverParams.Add(GenerateParameter(ChangeType.Add, name: $"CustomObjectSync/Temp/{name}{names[p]}", value: 1 * Mathf.Pow(0.5f, b + 1)));
							}
						}
					}
					state.behaviours = new StateMachineBehaviour[]
					{
						GenerateParameterDriver(parameterDriverParams.ToArray())
					};
					states.Add(GenerateChildState(new Vector3((-100) + multiplier * (b + 2) * 300, s * 50 ), state));
				}
			}
			
			#endregion
			
			#region transitions

			initialState.state.transitions = initialState.state.transitions.Append(GenerateTransition("", destinationState: startState.state, conditions: new []{ GenerateCondition(AnimatorConditionMode.If, $"CustomObjectSync/Start{mode}", 0), GenerateCondition( read ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, "IsLocal", 0)})).ToArray();
			
			startState.state.transitions = bitStates[0].Select((x, index) => GenerateBitTransition(permutations[index], 0, name, x.state, read)).ToArray();

			for (var i = 0; i < bitStates.Count; i++)
			{
				if (i == bitStates.Count - 1)
				{
					for (var s = 0; s < bitStates[i].Count; s++)
					{
						bitStates[i][s].state.transitions = new[]
						{
							GenerateTransition("", destinationState: endState.state, hasExitTime: true, exitTime: 1)
						};
					}
					continue;
				}

				for (int s1 = 0; s1 < bitStates[i].Count; s1++)
				{
					AnimatorStateTransition[] transitions = new AnimatorStateTransition[bitStates[i + 1].Count];
					for (int s2 = 0; s2 < bitStates[i+1].Count; s2++)
					{
						transitions[s2] = GenerateBitTransition(permutations[s2], i + 1, name, bitStates[i + 1][s2].state, read);
					}
					bitStates[i][s1].state.transitions = transitions;
				}
			}

			endState.state.transitions = new[] { GenerateTransition("", destinationState: initialState.state, hasExitTime: true, exitTime: 1.0f) };
			
			#endregion
			
			machine.states = machine.states.Concat(new []{ startState, endState }).Concat(bitStates.SelectMany(x => x)).ToArray();
		}

		public void Remove()
		{
			VRCAvatarDescriptor descriptor = syncObject.GetComponentInParent<VRCAvatarDescriptor>();
			
			if (descriptor == null) return;

			if (descriptor.expressionsMenu != null && descriptor.expressionsMenu.controls != null &&
			    descriptor.expressionsMenu.controls.Any(x => x.parameter.name == "CustomObjectSync/Enabled"))
			{
				descriptor.expressionsMenu.controls = descriptor.expressionsMenu.controls
					.Where(x => x.parameter.name != "CustomObjectSync/Enabled").ToList();
			}

			if (descriptor.expressionParameters != null && descriptor.expressionParameters.parameters != null &&
			    descriptor.expressionParameters.parameters.Any(x => x.name.Contains("CustomObjectSync")))
			{
				descriptor.expressionParameters.parameters = descriptor.expressionParameters.parameters
					.Where(x => !x.name.Contains("CustomObjectSync/")).ToArray();
			}

			if (descriptor != null &&
			    descriptor.baseAnimationLayers.FirstOrDefault(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX)
				    .animatorController != null &&
			    ((AnimatorController)descriptor.baseAnimationLayers
				    .FirstOrDefault(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController).layers
			    .Any(x => x.name.Contains("CustomObjectSync")))
			{
				AnimatorController controller = ((AnimatorController)descriptor.baseAnimationLayers
					.FirstOrDefault(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController);
				string[] layerNames = new[] {"CustomObjectSync/Parameter Initialization", "CustomObjectSync/Position Bit Convert" , "CustomObjectSync/Rotation Bit Convert", "CustomObjectSync/Sync" };
				controller.layers = controller.layers.Where(x => !layerNames.Contains(x.name)).ToArray();
				controller.parameters = controller.parameters.Where(x => !x.name.Contains("CustomObjectSync/")).ToArray();
			}
			
			if (descriptor.transform.Find("Custom Object Sync"))
			{
				Transform prefab = descriptor.transform.Find("Custom Object Sync");
				Transform userObj = Enumerable.Range(0, prefab.childCount)
					.Select(x => prefab.GetChild(x)).FirstOrDefault(x => x.name != "Set" && x.name != "Measure");
				if (userObj != null)
				{
					ParentConstraint parentConstraint = userObj.GetComponent<ParentConstraint>();
					if (parentConstraint != null)
					{
						Transform target = Enumerable.Range(0, parentConstraint.sourceCount)
							.Select(x => parentConstraint.GetSource(x)).Where(x => x.sourceTransform.name.EndsWith("Target"))
							.Select(x => x.sourceTransform).FirstOrDefault();
						if (target != null)
						{
							string oldPath = AnimationUtility.CalculateTransformPath(userObj.transform, descriptor.transform);
							userObj.parent = target.parent.transform;
							string newPath = AnimationUtility.CalculateTransformPath(userObj.transform, descriptor.transform);
			
							AnimationClip[] allClips = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
								.Where(x => x.animatorController != null).SelectMany(x => x.animatorController.animationClips)
								.ToArray();
							AnimatorUtils.RenameClipPaths(allClips, false, oldPath, newPath);
						}

						GameObject.DestroyImmediate(target.gameObject);
					}
				}
				Transform result = prefab.Find("Set/Result");
				if (result != null)
				{
					foreach (ParentConstraint positionConstraint in descriptor.transform.GetComponentsInChildren<ParentConstraint>()
						         .Where(x =>
						         {
							         List<ConstraintSource> sources = new List<ConstraintSource>();
							         x.GetSources(sources);
							         return sources.Any(y => y.sourceTransform == result);
						         }).ToArray())
					{
						GameObject.DestroyImmediate(positionConstraint);
					}
				}
				GameObject.DestroyImmediate(prefab.gameObject);
			}
			
			EditorUtility.DisplayDialog("Success!", "Custom Object Sync has been successfully removed", "Ok");
		}

		public int GetMaxBitCount()
		{
			int rotationBits = rotationEnabled ? (rotationPrecision * 3) : 0;
			int positionBits = 3 * (maxRadius + positionPrecision);
			int maxbitCount = rotationBits + positionBits;
			return maxbitCount;
		}

		public AnimatorStateTransition GenerateBitTransition(bool[] values, int index, string parameterName, AnimatorState destinationState, bool read)
		{
			AnimatorStateTransition transition = null;
			if (read)
			{
				transition = 
					GenerateTransition("", destinationState: destinationState, conditions: Enumerable.Range(0, values.Length).Select(i =>
						GenerateCondition(
							values[i] ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less, 
							$"CustomObjectSync/Temp/{parameterName}{names[i]}" , 
							Mathf.Pow(0.5f, index + 1) * (values[i] ? 0.9999f : 1.0001f))
					).ToArray());
			}
			else
			{
				transition = 
					GenerateTransition("", destinationState: destinationState, conditions: Enumerable.Range(0, values.Length).Select(i =>
						GenerateCondition(
							values[i] ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 
							$"CustomObjectSync/Bits/{parameterName}{names[i]}{index}" , 
							0)
						).ToArray());
			}
			
			

			return transition;
		}
		
		bool[][] GeneratePermutations(int size)
		{
			return Enumerable.Range(0, (int)Math.Pow(2, size)).Select(i => Enumerable.Range(0, size).Select(b => ((i & (1 << b)) > 0)).ToArray()).ToArray();
		}
		
	}
}