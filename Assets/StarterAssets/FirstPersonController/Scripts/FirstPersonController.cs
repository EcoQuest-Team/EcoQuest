using UnityEngine;
using UnityEngine.InputSystem;

namespace StarterAssets
{
	[RequireComponent(typeof(CharacterController))]
	[RequireComponent(typeof(PlayerInput))]
	public class FirstPersonController : MonoBehaviour
	{
		[Header("Player")]
		[Tooltip("Slow walk speed of the character in m/s (Left Alt)")]
		public float WalkSpeed = 2.0f;
		[Tooltip("Move speed of the character in m/s (default Walk)")]
		public float MoveSpeed = 2.0f;
		[Tooltip("Sprint speed of the character in m/s (Run)")]
		public float SprintSpeed = 5.8f;
		[Tooltip("Rotation speed of the character")]
		public float RotationSpeed = 1.0f;
		[Tooltip("Acceleration and deceleration")]
		public float SpeedChangeRate = 10.0f;

		[Space(10)]
		[Tooltip("The height the player can jump")]
		public float JumpHeight = 1.2f;
		[Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
		public float Gravity = -15.0f;
		[Tooltip("Coyote time window after leaving ground where jump is still allowed")]
		public float CoyoteTime = 0.1f;
		[Tooltip("Jump input buffer window before landing")]
		public float JumpBufferTime = 0.12f;

		[Space(10)]
		[Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
		public float JumpTimeout = 0.1f;
		[Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
		public float FallTimeout = 0.15f;

		[Header("Player Grounded")]
		[Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
		public bool Grounded = true;
		[Tooltip("Useful for rough ground")]
		public float GroundedOffset = -0.14f;
		[Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
		public float GroundedRadius = 0.28f;
		[Tooltip("Extra degrees above CharacterController.slopeLimit tolerated as walkable ground")]
		public float GroundSlopeTolerance = 5.0f;
		[Tooltip("What layers the character uses as ground")]
		public LayerMask GroundLayers;

		[Header("Animation (Single Parameter Source)")]
		public Animator CharacterAnimator;
		public float SpeedDampTime = 0.10f;
		public float IdleVariationInterval = 8.0f;

		[Header("Locomotion Differences & Feel")]
		[Tooltip("Animator speed multiplier for walk")]
		public float WalkAnimSpeed = 1.0f;
		[Tooltip("Animator speed multiplier for run")]
		public float RunAnimSpeed = 1.15f;
		[Tooltip("Procedural forward body lean (degrees) when running")]
		public float RunSpineLean = 10.0f;
		[Tooltip("Smoothing time for spine lean")]
		public float SpineLeanDampTime = 0.10f;
		[Tooltip("Footstep interval while walking")]
		public float WalkFootstepInterval = 0.48f;
		[Tooltip("Footstep interval while running")]
		public float RunFootstepInterval = 0.26f;

		[Header("Cinemachine")]
		[Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
		public GameObject CinemachineCameraTarget;
		[Tooltip("How far in degrees can you move the camera up")]
		public float TopClamp = 90.0f;
		[Tooltip("How far in degrees can you move the camera down")]
		public float BottomClamp = -90.0f;

		[Header("Audio (Routed to SFX Mixer Group)")]
		public AudioSource FootstepAudioSource;
		public AudioClip LandingAudioClip;
		public AudioClip[] FootstepAudioClips;
		[Range(0f, 1f)] public float FootstepAudioVolume = 0.5f;
		public float FootstepInterval = 0.45f;

		// cinemachine
		private float _cinemachineTargetPitch;

		// player
		private float _speed;
		private float _rotationVelocity;
		private float _verticalVelocity;
		private float _terminalVelocity = 53.0f;
		private float _stepTimer;
		private bool _wasGrounded = true;
		private Transform _spineTransform;
		private float _currentSpineLean;
		private float _spineLeanVelocity;

		// jump coyote & buffer
		private float _lastGroundedTime = -999f;
		private float _jumpBufferTimer;
		private float _baseColliderHeight = 1.75f;
		private Vector3 _baseColliderCenter = new Vector3(0f, 0.88f, 0f);
		private bool _manualCarrying;
		private float _idleTimer;
		private float _nextIdleTime = 8.0f;
		private readonly Collider[] _groundHits = new Collider[8];

		// timeout deltatime
		private float _jumpTimeoutDelta;
		private float _fallTimeoutDelta;

		private PlayerInput _playerInput;
		private CharacterController _controller;
		private StarterAssetsInputs _input;
		private GameObject _mainCamera;

		private const float _threshold = 0.01f;

		// Animator parameter hashes (driven exclusively here)
		private static readonly int HashSpeed = Animator.StringToHash("Speed");
		private static readonly int HashGrounded = Animator.StringToHash("Grounded");
		private static readonly int HashVerticalVelocity = Animator.StringToHash("VerticalVelocity");
		private static readonly int HashJump = Animator.StringToHash("Jump");
		private static readonly int HashIdleIndex = Animator.StringToHash("IdleIndex");
		private static readonly int HashIsCarrying = Animator.StringToHash("IsCarrying");
		private static readonly int HashPickUp = Animator.StringToHash("PickUp");
		private static readonly int HashDrop = Animator.StringToHash("Drop");
		private static readonly int HashInteract = Animator.StringToHash("Interact");
		private static readonly int HashWave = Animator.StringToHash("Wave");

		public float VerticalVelocity => _verticalVelocity;
		public float CurrentHorizontalSpeed { get; private set; }
		public bool IsCarrying { get; private set; }

		private bool IsCurrentDeviceMouse
		{
			get
			{
				return _playerInput != null && _playerInput.currentControlScheme == "KeyboardMouse";
			}
		}

		private void Awake()
		{
			if (_mainCamera == null)
			{
				_mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
			}
			_controller = GetComponent<CharacterController>();
			if (_controller != null)
			{
				if (_controller.height < 1.5f)
				{
					_controller.height = 1.80f;
					_controller.center = new Vector3(0f, 0.90f, 0f);
				}
				_baseColliderHeight = _controller.height;
				_baseColliderCenter = _controller.center;
			}
			ResolveAnimator();
		}

		private void Start()
		{
			if (_controller == null) _controller = GetComponent<CharacterController>();
			_input = GetComponent<StarterAssetsInputs>();
			_playerInput = GetComponent<PlayerInput>();
			if (FootstepAudioSource == null) FootstepAudioSource = GetComponent<AudioSource>();
			ResolveAnimator();

			_jumpTimeoutDelta = JumpTimeout;
			_fallTimeoutDelta = FallTimeout;
			_wasGrounded = Grounded;
			_lastGroundedTime = Time.time;
			_nextIdleTime = IdleVariationInterval;
		}

		private void ResolveAnimator()
		{
			if (CharacterAnimator == null)
			{
				Transform vis = transform.Find("Visual");
				if (vis != null) CharacterAnimator = vis.GetComponent<Animator>();
				if (CharacterAnimator == null) CharacterAnimator = GetComponentInChildren<Animator>();
			}
			if (CharacterAnimator != null)
			{
				CharacterAnimator.applyRootMotion = false;
				CharacterAnimator.updateMode = AnimatorUpdateMode.Normal;
				if (CharacterAnimator.isHuman)
				{
					_spineTransform = CharacterAnimator.GetBoneTransform(HumanBodyBones.Spine);
				}
			}
		}

		private void Update()
		{
			GroundedCheck();
			JumpAndGravity();
			Move();
			UpdateAnimator(Time.deltaTime);
			UpdateFootstepAudio();
		}

		private void UpdateFootstepAudio()
		{
			if (!_wasGrounded && Grounded)
			{
				PlayLandingSound();
			}
			_wasGrounded = Grounded;

			if (Grounded && _input != null && _input.move.sqrMagnitude > 0.01f && _speed > 0.1f)
			{
				float runFactor = Mathf.Clamp01((CurrentHorizontalSpeed - (MoveSpeed + 0.1f)) / Mathf.Max(0.1f, SprintSpeed - MoveSpeed));
				float interval = Mathf.Lerp(WalkFootstepInterval, RunFootstepInterval, runFactor);

				_stepTimer += Time.deltaTime;
				if (_stepTimer >= interval)
				{
					_stepTimer = 0f;
					PlayFootstepSound();
				}
			}
			else
			{
				_stepTimer = 0f;
			}
		}

		public void PlayFootstepSound()
		{
			if (FootstepAudioClips == null || FootstepAudioClips.Length == 0) return;
			AudioClip clip = FootstepAudioClips[Random.Range(0, FootstepAudioClips.Length)];
			if (clip == null) return;

			if (FootstepAudioSource != null)
			{
				FootstepAudioSource.PlayOneShot(clip, FootstepAudioVolume);
			}
		}

		public void PlayLandingSound()
		{
			if (LandingAudioClip == null) return;
			if (FootstepAudioSource != null)
			{
				FootstepAudioSource.PlayOneShot(LandingAudioClip, FootstepAudioVolume);
			}
		}

		private void LateUpdate()
		{
			CameraRotation();

			if (_spineTransform != null && Grounded)
			{
				float runFactor = Mathf.Clamp01((CurrentHorizontalSpeed - (MoveSpeed + 0.1f)) / Mathf.Max(0.1f, SprintSpeed - MoveSpeed));
				float targetLean = runFactor * RunSpineLean;
				_currentSpineLean = Mathf.SmoothDamp(_currentSpineLean, targetLean, ref _spineLeanVelocity, SpineLeanDampTime);
				if (_currentSpineLean > 0.02f)
				{
					_spineTransform.localRotation *= Quaternion.Euler(_currentSpineLean, 0f, 0f);
				}
			}
		}

		private void GroundedCheck()
		{
			if (_verticalVelocity > 0.5f)
			{
				Grounded = false;
				return;
			}

			int mask = GroundLayers.value != 0
				? (GroundLayers.value | ~LayerMask.GetMask("Ignore Raycast", "UI", "Player"))
				: ~LayerMask.GetMask("Ignore Raycast", "UI", "Player");

			float checkRadius = _controller != null ? Mathf.Min(GroundedRadius, _controller.radius * 0.95f) : GroundedRadius;
			Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);

			bool sphereGrounded = false;
			int hitCount = Physics.OverlapSphereNonAlloc(spherePosition, checkRadius, _groundHits, mask, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < hitCount; i++)
			{
				Collider col = _groundHits[i];
				if (col == null || col.transform == transform || col.transform.IsChildOf(transform)) continue;
				sphereGrounded = true;
				break;
			}

			bool rawGrounded = sphereGrounded || (_controller != null && _controller.isGrounded);
			if (rawGrounded && _controller != null)
			{
				float maxSlope = _controller.slopeLimit + GroundSlopeTolerance;
				Vector3 castOrigin = transform.position + Vector3.up * (_controller.radius + 0.25f);
				if (Physics.SphereCast(castOrigin, checkRadius * 0.85f, Vector3.down, out RaycastHit slopeHit, 0.55f, mask, QueryTriggerInteraction.Ignore))
				{
					if (slopeHit.collider != null && slopeHit.transform != transform && !slopeHit.transform.IsChildOf(transform))
					{
						float slopeAngle = Vector3.Angle(slopeHit.normal, Vector3.up);
						if (slopeAngle > maxSlope && !_controller.isGrounded)
						{
							rawGrounded = false;
						}
					}
				}
			}

			Grounded = rawGrounded;
			if (Grounded)
			{
				_lastGroundedTime = Time.time;
			}
		}

		private void CameraRotation()
		{
			if (_input.look.sqrMagnitude >= _threshold)
			{
				float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;

				_cinemachineTargetPitch += _input.look.y * RotationSpeed * deltaTimeMultiplier;
				_rotationVelocity = _input.look.x * RotationSpeed * deltaTimeMultiplier;

				_cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

				CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_cinemachineTargetPitch, 0.0f, 0.0f);
				transform.Rotate(Vector3.up * _rotationVelocity);
			}
		}

		private void Move()
		{
			bool slowWalk = (Keyboard.current != null && Keyboard.current.leftAltKey.isPressed) || Input.GetKey(KeyCode.LeftAlt);
			float carrySpeedMult = DragAndDrop.IsAnyItemGrabbed ? 0.85f : 1.0f;
			float baseTargetSpeed = _input.sprint ? SprintSpeed : (slowWalk ? WalkSpeed : MoveSpeed);
			float targetSpeed = baseTargetSpeed * carrySpeedMult;

			if (_input.move == Vector2.zero) targetSpeed = 0.0f;

			float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;
			float speedOffset = 0.1f;
			float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

			if (currentHorizontalSpeed < targetSpeed - speedOffset || currentHorizontalSpeed > targetSpeed + speedOffset)
			{
				_speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude, Time.deltaTime * SpeedChangeRate);
				_speed = Mathf.Round(_speed * 1000f) / 1000f;
			}
			else
			{
				_speed = targetSpeed;
			}

			Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;
			if (_input.move != Vector2.zero)
			{
				inputDirection = transform.right * _input.move.x + transform.forward * _input.move.y;
			}

			_controller.Move(inputDirection.normalized * (_speed * Time.deltaTime) + new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);

			float measuredHoriz = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;
			CurrentHorizontalSpeed = (_input.move == Vector2.zero && measuredHoriz < 0.05f) ? 0f : Mathf.Max(measuredHoriz, _input.move != Vector2.zero ? _speed * 0.85f : 0f);
		}

		private void JumpAndGravity()
		{
			float dt = Time.deltaTime;

			if (_input != null && _input.jump)
			{
				_jumpBufferTimer = JumpBufferTime;
				_input.jump = false;
			}
			else if (_jumpBufferTimer > 0.0f)
			{
				_jumpBufferTimer -= dt;
			}

			bool withinCoyote = Grounded || ((Time.time - _lastGroundedTime) <= CoyoteTime);

			if (Grounded)
			{
				_fallTimeoutDelta = FallTimeout;

				if (_verticalVelocity < 0.0f)
				{
					_verticalVelocity = -2f;
				}

				if (_jumpTimeoutDelta >= 0.0f)
				{
					_jumpTimeoutDelta -= dt;
				}
			}
			else
			{
				if (!withinCoyote)
				{
					_jumpTimeoutDelta = JumpTimeout;
				}

				if (_fallTimeoutDelta >= 0.0f)
				{
					_fallTimeoutDelta -= dt;
				}
			}

			if (_jumpBufferTimer > 0.0f && withinCoyote && _jumpTimeoutDelta <= 0.0f)
			{
				_verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
				_jumpBufferTimer = 0.0f;
				_lastGroundedTime = -999f;
				_jumpTimeoutDelta = JumpTimeout;
				Grounded = false;
				TriggerJumpAnimation();
			}

			if (_verticalVelocity < _terminalVelocity)
			{
				_verticalVelocity += Gravity * dt;
			}
		}

		private void UpdateAnimator(float dt)
		{
			if (dt <= 0f) return;
			if (CharacterAnimator == null) ResolveAnimator();
			if (CharacterAnimator == null || CharacterAnimator.runtimeAnimatorController == null) return;

			bool carrying = (DragAndDrop.CurrentlyGrabbedItem != null && DragAndDrop.CurrentlyGrabbedItem.IsGrabbed)
				|| ItemCarry.ActiveCarriedItem != null
				|| _manualCarrying;
			IsCarrying = carrying;

			CharacterAnimator.SetFloat(HashSpeed, CurrentHorizontalSpeed, SpeedDampTime, dt);
			if (CurrentHorizontalSpeed < 0.01f && CharacterAnimator.GetFloat(HashSpeed) < 0.02f)
			{
				CharacterAnimator.SetFloat(HashSpeed, 0f);
			}

			// Animator speed multiplier: walk 1.0, run 1.15
			float runT = Mathf.Clamp01((CurrentHorizontalSpeed - (MoveSpeed + 0.1f)) / Mathf.Max(0.1f, SprintSpeed - MoveSpeed));
			CharacterAnimator.speed = Mathf.Lerp(WalkAnimSpeed, RunAnimSpeed, runT);

			CharacterAnimator.SetBool(HashGrounded, Grounded);
			if (Grounded)
			{
				CharacterAnimator.ResetTrigger(HashJump);
			}
			CharacterAnimator.SetFloat(HashVerticalVelocity, _verticalVelocity);
			CharacterAnimator.SetBool(HashIsCarrying, carrying);

			if (CharacterAnimator.layerCount > 1)
			{
				bool upperBodyActive = carrying;
				if (!upperBodyActive)
				{
					AnimatorStateInfo layer1State = CharacterAnimator.GetCurrentAnimatorStateInfo(1);
					upperBodyActive = !layer1State.IsName("Empty");
				}
				float targetLayerWeight = upperBodyActive ? 1f : 0f;
				float currentLayerWeight = CharacterAnimator.GetLayerWeight(1);
				CharacterAnimator.SetLayerWeight(1, Mathf.MoveTowards(currentLayerWeight, targetLayerWeight, dt * 10f));
			}

			if (!carrying)
			{
				CharacterAnimator.ResetTrigger(HashPickUp);
			}

			// Random idle variations after ~8s when grounded and stationary
			bool stationaryIdle = Grounded && !carrying && CurrentHorizontalSpeed < 0.05f && (_input == null || _input.move == Vector2.zero);
			if (stationaryIdle)
			{
				AnimatorStateInfo st = CharacterAnimator.GetCurrentAnimatorStateInfo(0);
				if (st.IsName("IdleVar1") || st.IsName("IdleVar2"))
				{
					if (st.normalizedTime >= 0.85f && CharacterAnimator.GetInteger(HashIdleIndex) != 0)
					{
						CharacterAnimator.SetInteger(HashIdleIndex, 0);
						_idleTimer = 0f;
						_nextIdleTime = Random.Range(IdleVariationInterval * 0.9f, IdleVariationInterval * 1.15f);
					}
				}
				else if (CharacterAnimator.GetInteger(HashIdleIndex) == 0)
				{
					_idleTimer += dt;
					if (_idleTimer >= _nextIdleTime)
					{
						_idleTimer = 0f;
						CharacterAnimator.SetInteger(HashIdleIndex, Random.Range(1, 3));
					}
				}
			}
			else
			{
				_idleTimer = 0f;
				if (CharacterAnimator.GetInteger(HashIdleIndex) != 0)
				{
					CharacterAnimator.SetInteger(HashIdleIndex, 0);
				}
			}
		}

		public void SetCarryingState(bool carrying)
		{
			_manualCarrying = carrying;
			IsCarrying = carrying;
		}

		public void TriggerJumpAnimation()
		{
			if (CharacterAnimator != null && CharacterAnimator.runtimeAnimatorController != null)
			{
				CharacterAnimator.ResetTrigger(HashJump);
				CharacterAnimator.SetTrigger(HashJump);
			}
		}

		public void TriggerPickUpAnimation()
		{
			if (CharacterAnimator != null && CharacterAnimator.runtimeAnimatorController != null)
			{
				if (CharacterAnimator.layerCount > 1) CharacterAnimator.SetLayerWeight(1, 1f);
				CharacterAnimator.SetTrigger(HashPickUp);
			}
		}

		public void TriggerDropAnimation()
		{
			if (CharacterAnimator != null && CharacterAnimator.runtimeAnimatorController != null)
			{
				if (CharacterAnimator.layerCount > 1) CharacterAnimator.SetLayerWeight(1, 1f);
				CharacterAnimator.SetTrigger(HashDrop);
			}
		}

		public void TriggerInteractAnimation()
		{
			if (CharacterAnimator != null && CharacterAnimator.runtimeAnimatorController != null)
			{
				if (CharacterAnimator.layerCount > 1) CharacterAnimator.SetLayerWeight(1, 1f);
				CharacterAnimator.SetTrigger(HashInteract);
			}
		}

		public void TriggerWaveAnimation()
		{
			if (!IsCarrying && CharacterAnimator != null && CharacterAnimator.runtimeAnimatorController != null)
			{
				if (CharacterAnimator.layerCount > 1) CharacterAnimator.SetLayerWeight(1, 1f);
				CharacterAnimator.SetTrigger(HashWave);
			}
		}

		private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
		{
			if (lfAngle < -360f) lfAngle += 360f;
			if (lfAngle > 360f) lfAngle -= 360f;
			return Mathf.Clamp(lfAngle, lfMin, lfMax);
		}

		private void OnDrawGizmosSelected()
		{
			Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
			Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

			if (Grounded) Gizmos.color = transparentGreen;
			else Gizmos.color = transparentRed;

			Gizmos.DrawSphere(new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z), GroundedRadius);
		}
	}
}
