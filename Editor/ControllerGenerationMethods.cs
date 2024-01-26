#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using static VRC.SDKBase.VRC_AnimatorTrackingControl;
using static VRC.SDKBase.VRC_AvatarParameterDriver;
using Object = UnityEngine.Object;

namespace VRLabs.CustomObjectSyncCreator
{
	public class ControllerGenerationMethods
	{
		public static AnimationClip GenerateClip(string name, Bounds localBounds = default, float frameRate = 60f, WrapMode wrapMode = WrapMode.Default, AnimationEvent[] events = null)
		{
			return new AnimationClip()
			{
				name = name,
				wrapMode = wrapMode,
				localBounds = localBounds,
				frameRate = frameRate,
				legacy = false
			};
		}

		public static AnimationCurve GenerateCurve(Keyframe[] keys, WrapMode preWrapMode = WrapMode.ClampForever, WrapMode postWrapMode = WrapMode.ClampForever)
		{
			return new AnimationCurve()
			{
				keys = keys,
				preWrapMode = preWrapMode,
				postWrapMode = postWrapMode
			};
		}
				
		public static ObjectReferenceKeyframe GenerateObjectReferenceKeyFrame(float time, Object obj)
		{
			return new ObjectReferenceKeyframe()
			{
				time = time,
				value = obj
			};
		}

		public static Keyframe GenerateKeyFrame(float time = 0, float value = 0, float inTangent = 0, float outTangent = 0, float inWeight = 0, float outWeight = 0)
		{
			Keyframe frame = new Keyframe()
			{
				time = time,
				value = value,
				inTangent = inTangent,
				outTangent = outTangent,
				inWeight = inWeight,
				outWeight = outWeight,
			};
			frame.weightedMode = (inWeight != 0 || outWeight != 0) ? WeightedMode.Both : WeightedMode.None;
			return frame;
		}
			
		// Redundant? Maybe. But I'm gonna use it anyways hehe
		public static void AddCurve(AnimationClip clip, string relativePath, Type type, string propertyName, AnimationCurve curve)
		{
			clip.SetCurve(relativePath, type, propertyName, curve);
		}
		
        public static void AddObjectCurve(AnimationClip clip, string relativePath, Type type, string propertyName, ObjectReferenceKeyframe[] curve)
        {
            AnimationUtility.SetObjectReferenceCurve(clip, new EditorCurveBinding()
            {
                path = relativePath,
                propertyName = propertyName,
                type = type
            }, curve);	
        }

        public static BlendTree GenerateBlendTree(string name, BlendTreeType blendType, string blendParameter = "", string blendParameterY = "", float minThreshold = 0, float maxThreshold = 1, bool useAutomaticThresholds = true)
        {
            return new BlendTree()
            {
                name = name,
                blendType = blendType,
                blendParameter = blendParameter,
                blendParameterY = blendParameterY,
                maxThreshold = maxThreshold,
                minThreshold = minThreshold,
                useAutomaticThresholds = useAutomaticThresholds
            };
        }

        public static ChildMotion GenerateChildMotion(Motion motion = null, Vector2 position = default, float threshold = 0.0f, float timeScale = 1.0f, string directBlendParameter = "", float cycleOffset = 0.0f, bool mirror = false)
        {
            return new ChildMotion()
            {
                motion = motion,
                position = position,
                threshold = threshold,
                timeScale = timeScale,
                directBlendParameter = directBlendParameter,
                cycleOffset = cycleOffset,
                mirror = mirror
            };
        }
        		
		
		public static AnimatorStateMachine GenerateStateMachine(string name, 
			Vector3 anyStatePosition, Vector3 entryPosition, Vector3 exitPosition,
			ChildAnimatorState[] states = null, AnimatorState defaultState = null, 
			StateMachineBehaviour[] behaviours = null,
			AnimatorStateTransition[] anyStateTransitions = null, 
			AnimatorTransition[] entryTransitions = null, 
			Vector3 parentStateMachinePosition = default, ChildAnimatorStateMachine[] stateMachines = null)
		{
			return new AnimatorStateMachine()
			{
				name = name,
				anyStatePosition = anyStatePosition,
				entryPosition = entryPosition,
				exitPosition = exitPosition,
				states = states ?? Array.Empty<ChildAnimatorState>(),
				defaultState = defaultState,
				behaviours = behaviours ?? Array.Empty<StateMachineBehaviour>(),
				anyStateTransitions = anyStateTransitions ?? Array.Empty<AnimatorStateTransition>(),
				entryTransitions = entryTransitions ?? Array.Empty<AnimatorTransition>(),
				parentStateMachinePosition = parentStateMachinePosition,
				stateMachines = stateMachines
			};
		}
		
        public static ChildAnimatorStateMachine GenerateChildStateMachine(Vector3 position, AnimatorStateMachine stateMachine)
        {
            return new ChildAnimatorStateMachine()
            {
                position = position,
                stateMachine = stateMachine
            };
        }
		
		public static AnimatorState GenerateState(string name, bool writeDefaultValues = false, string tag = "", 
			Motion motion = null, AnimatorStateTransition[] transitions = null,
			float cycleOffset = 0.0f, string cycleOffsetParameter = "", bool cycleOffsetParameterActive = false,
			bool mirror = false, string mirrorParameter = "", bool mirrorParameterActive = false,
			string timeParameter = "",  bool timeParameterActive = false,
			float speed = 1.0f, string speedParameter = "", bool speedParameterActive = false
			)
		{
			return new AnimatorState()
			{
				name = name,
				writeDefaultValues = writeDefaultValues,
				tag = tag,
				motion = motion,
				transitions = transitions ?? Array.Empty<AnimatorStateTransition>(),
				cycleOffset = cycleOffset,
				cycleOffsetParameter = cycleOffsetParameter,
				cycleOffsetParameterActive = cycleOffsetParameterActive,
				mirror = mirror,
				mirrorParameter = mirrorParameter,
				mirrorParameterActive = mirrorParameterActive,
				timeParameter = timeParameter,
				timeParameterActive = timeParameterActive,
				speed = speed,
				speedParameter = speedParameter,
				speedParameterActive = speedParameterActive
			};
		}
		
		public static ChildAnimatorState GenerateChildState(Vector3 position, AnimatorState state)
		{
			return new ChildAnimatorState()
			{
				position = position,
				state = state
			};
		}

		public static AnimatorStateTransition GenerateTransition(string name, bool canTransitionToSelf = false, AnimatorCondition[] conditions = null, 
			AnimatorState destinationState = null, AnimatorStateMachine destinationStateMachine = null,
			float duration = 0.0f, bool hasFixedDuration = false,
			float exitTime = 0.0f, bool hasExitTime = false,
			bool solo = false, bool mute = false, bool isExit = false,
			float offset = 0.0f, bool orderedInterruption = true, TransitionInterruptionSource interruptionSource = TransitionInterruptionSource.None)
		{
			return new AnimatorStateTransition()
			{
				name = name,
				canTransitionToSelf = canTransitionToSelf,
				conditions = conditions ?? Array.Empty<AnimatorCondition>(),
				destinationState = destinationState,
				destinationStateMachine = destinationStateMachine,
				duration = duration,
				hasFixedDuration = hasFixedDuration,
				exitTime = exitTime,
				hasExitTime = hasExitTime,
				solo = solo,
				mute = mute,
				isExit = isExit,
				offset = offset,
				orderedInterruption = orderedInterruption,
				interruptionSource = interruptionSource,
			};
		}

		public static AnimatorTransition GenerateStateMachineTransition(string name, AnimatorCondition[] conditions = null, 
			AnimatorState destinationState = null, AnimatorStateMachine destinationStateMachine = null,
			bool solo = false, bool mute = false, bool isExit = false)
		{
			return new AnimatorTransition()
			{
				name = name,
				conditions = conditions ?? Array.Empty<AnimatorCondition>(),
				destinationState = destinationState,
				destinationStateMachine = destinationStateMachine,
				solo = solo,
				mute = mute,
				isExit = isExit,
			};
		}

		public static AnimatorCondition GenerateCondition(AnimatorConditionMode mode, string parameter, float threshold)
		{
			return new AnimatorCondition()
			{
				mode = mode,
				parameter = parameter,
				threshold = threshold
			};
		}

		public static AnimatorControllerLayer GenerateLayer(string name, AnimatorStateMachine stateMachine, AvatarMask mask = null, AnimatorLayerBlendingMode blendingMode = AnimatorLayerBlendingMode.Override, 
			float defaultWeight = 1.0f, bool syncedLayerAffectsTiming = false, int syncedLayerIndex = -1)
		{
			return new AnimatorControllerLayer()
			{
				name = name,
				stateMachine = stateMachine,
				avatarMask = mask,
				blendingMode = blendingMode,
				defaultWeight = defaultWeight,
				syncedLayerAffectsTiming = syncedLayerAffectsTiming,
				syncedLayerIndex = syncedLayerIndex
			};
		}

		public static AnimatorControllerParameter GenerateIntParameter(string name, int defaultInt = 0)
		{
			return new AnimatorControllerParameter()
			{
				name = name,
				defaultInt = defaultInt,
				type = AnimatorControllerParameterType.Int,
			};
		}
		
		public static AnimatorControllerParameter GenerateFloatParameter(string name, float defaultFloat = 0.0f)
		{
			return new AnimatorControllerParameter()
			{
				name = name,
				defaultFloat = defaultFloat,
				type = AnimatorControllerParameterType.Float,
			};
		}
		
		public static AnimatorControllerParameter GenerateBoolParameter(string name, bool defaultBool = false)
		{
			return new AnimatorControllerParameter()
			{
				name = name,
				defaultBool = defaultBool,
				type = AnimatorControllerParameterType.Bool,
			};
		}
		
		public static AnimatorControllerParameter GenerateTriggerParameter(string name)
		{
			return new AnimatorControllerParameter()
			{
				name = name,
				type = AnimatorControllerParameterType.Trigger,
			};
		}

		public static AvatarMask GenerateMask(string name, string[] transforms, bool[] values, AvatarMaskBodyPart[] bodyParts)
		{
			AvatarMask mask = new AvatarMask()
			{
				name = name,
				transformCount = transforms.Length
			};

			for (var i = 0; i < transforms.Length; i++)
			{
				mask.SetTransformPath(i, transforms[i]);
				mask.SetTransformActive(i, values[i]);
			}

			foreach (var part in Enum.GetValues(typeof(AvatarMaskBodyPart)).Cast<AvatarMaskBodyPart>().Where(x => x != AvatarMaskBodyPart.LastBodyPart))
			{
				mask.SetHumanoidBodyPartActive(part, bodyParts.Contains(part));
			}
			
			return mask;
		}


		public static VRCAvatarParameterDriver GenerateParameterDriver(Parameter[] parameters, bool isEnabled = true, bool localOnly = false, string debugString = "")
		{
			VRCAvatarParameterDriver driver = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
			driver.parameters = parameters.ToList();
			driver.debugString = debugString;
			driver.isEnabled = isEnabled;
			driver.localOnly = localOnly;
			return driver;
		}

		public static Parameter GenerateParameter(ChangeType type, string source = "", string name = "", float value = 0f, float chance = 0f, bool convertRange = false, float destMax = 0, float destMin = 0,
			float sourceMin = 0, float sourceMax = 0, float valueMin = 0, float valueMax = 0)
		{
			return new Parameter()
			{
				type = type,
				source = source,
				name = name,
				value = value,
				chance = chance,
				convertRange = convertRange,
				destMax = destMax,
				destMin = destMin,
				sourceMin = sourceMin,
				sourceMax = sourceMax,
				valueMin = valueMin,
				valueMax = valueMax
			};
		}

		public static VRCAnimatorLayerControl GenerateAnimatorLayerControl(VRC_AnimatorLayerControl.BlendableLayer playable, int layer = 0, float blendDuration = 0, float goalWeight = 1, string debugString = "")
		{
			VRCAnimatorLayerControl control = ScriptableObject.CreateInstance<VRCAnimatorLayerControl>();
			control.playable = playable;
			control.layer = layer;
			control.blendDuration = blendDuration;
			control.goalWeight = goalWeight;
			control.debugString = debugString;
			return control;
		}

		public static VRCAnimatorLocomotionControl GenerateLocomotionControl(bool disableLocomotion, string debugString = "")
		{
			VRCAnimatorLocomotionControl control = ScriptableObject.CreateInstance<VRCAnimatorLocomotionControl>();
			control.disableLocomotion = disableLocomotion;
			control.debugString = debugString;
			return control;
		}

		public static VRCAnimatorTrackingControl GenerateTrackingControl(TrackingType trackingHead = TrackingType.NoChange,
			TrackingType trackingLeftHand = TrackingType.NoChange,
			TrackingType trackingRightHand = TrackingType.NoChange,
			TrackingType trackingHip = TrackingType.NoChange,
			TrackingType trackingLeftFoot = TrackingType.NoChange,
			TrackingType trackingRightFoot = TrackingType.NoChange,
			TrackingType trackingLeftFingers = TrackingType.NoChange,
			TrackingType trackingRightFingers = TrackingType.NoChange,
			TrackingType trackingEyes = TrackingType.NoChange,
			TrackingType trackingMouth = TrackingType.NoChange,
			string debugString = "")
		{
			VRCAnimatorTrackingControl control = ScriptableObject.CreateInstance<VRCAnimatorTrackingControl>();
			control.trackingHead = trackingHead;
			control.trackingLeftHand = trackingLeftHand;
			control.trackingRightHand = trackingRightHand;
			control.trackingHip = trackingHip;
			control.trackingLeftFoot = trackingLeftFoot;
			control.trackingRightFoot = trackingRightFoot;
			control.trackingLeftFingers = trackingLeftFingers;
			control.trackingRightFingers = trackingRightFingers;
			control.trackingEyes = trackingEyes;
			control.trackingMouth = trackingMouth;
			control.trackingEyes = trackingEyes;
			control.debugString = debugString;
			return control;
		}

		public static VRCPlayableLayerControl GeneratePlayableLayerControl(VRC_PlayableLayerControl.BlendableLayer layer, float blendDuration = 0, float goalWeight = 1, string debugString = "")
		{
			VRCPlayableLayerControl control = ScriptableObject.CreateInstance<VRCPlayableLayerControl>();
			control.layer = layer;
			control.blendDuration = blendDuration;
			control.goalWeight = goalWeight;
			control.debugString = debugString;
			return control;
		}

		public static VRCAnimatorTemporaryPoseSpace GenerateTemporaryPoseSpace(float delayTime = 0, bool fixedDelay = false, bool enterPoseSpace = true, string debugString = "")
		{
			VRCAnimatorTemporaryPoseSpace control = ScriptableObject.CreateInstance<VRCAnimatorTemporaryPoseSpace>();
			control.delayTime = delayTime;
			control.fixedDelay = fixedDelay;
			control.enterPoseSpace = enterPoseSpace;
			control.debugString = debugString;
			return control;
		}
		
		public static AnimatorController GenerateController(string name, AnimatorControllerLayer[] layers, AnimatorControllerParameter[] parameters)
		{
			return new AnimatorController()
			{
				name = name,
				layers = layers,
				parameters = parameters
			};
		}
		
		public static void SerializeController(AnimatorController controller)
		{
			controller.layers.ToList().ForEach(x => SerializeStateMachine(controller, x.stateMachine));
		}

		public static void SerializeStateMachine(AnimatorController controller, AnimatorStateMachine stateMachine)
		{
			Add(controller, stateMachine);
			foreach (var childAnimatorState in stateMachine.states)
			{
				AnimatorState animatorState = childAnimatorState.state;
				
				Add(controller, animatorState);
				animatorState.transitions.ToList().ForEach(x => Add(controller, x));
				animatorState.behaviours.ToList().ForEach(x => Add(controller, x));
				
				if (animatorState.motion is BlendTree tree)
				{
					Queue<BlendTree> trees = new Queue<BlendTree>(new [] {tree});
					while (trees.Count>0)
					{
						tree = trees.Dequeue();
						AssetDatabase.RemoveObjectFromAsset(tree);
						Add(controller, tree);
						tree.children.Where(x => x.motion is BlendTree).ToList().ForEach(x => trees.Enqueue((BlendTree)x.motion));
					}
				}
			}
			
			stateMachine.entryTransitions.ToList().ForEach(x => Add(controller, x));
			stateMachine.anyStateTransitions.ToList().ForEach(x => Add(controller, x));
			stateMachine.stateMachines.ToList().ForEach(x => SerializeStateMachine(controller, x.stateMachine));
		}
		
		public static void Add(AnimatorController controller, Object o){
			o.hideFlags = HideFlags.HideInHierarchy;
			AssetDatabase.RemoveObjectFromAsset(o);
			AssetDatabase.AddObjectToAsset(o, controller);
		}

		public static VRCExpressionParameters.Parameter GenerateVRCParameter(string name, VRCExpressionParameters.ValueType valueType, float defaultValue = 0.0f, bool saved = false, bool networkSynced = true)
		{
			return new VRCExpressionParameters.Parameter()
			{
				name = name,
				valueType = valueType,
				defaultValue = defaultValue,
				saved = saved,
				networkSynced = networkSynced
			};
		}
	}
}
#endif