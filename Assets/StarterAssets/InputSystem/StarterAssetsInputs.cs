using UnityEngine;
using UnityEngine.InputSystem;

namespace StarterAssets
{
	public class StarterAssetsInputs : MonoBehaviour
	{
		[Header("Character Input Values")]
		public Vector2 move;
		public Vector2 look;
		public bool jump;
		public bool sprint;

		[Header("Movement Settings")]
		public bool analogMovement;

		[Header("Mouse Cursor Settings")]
		public bool cursorLocked = true;
		public bool cursorInputForLook = true;

		public void OnMove(InputValue value)
		{
			MoveInput(value.Get<Vector2>());
		}

		public void OnLook(InputValue value)
		{
			if (CursorState.LookEnabled && cursorInputForLook)
			{
				LookInput(value.Get<Vector2>());
			}
			else
			{
				look = Vector2.zero;
			}
		}

		public void OnJump(InputValue value)
		{
			JumpInput(value.isPressed);
		}

		public void OnSprint(InputValue value)
		{
			SprintInput(value.isPressed);
		}


		public void MoveInput(Vector2 newMoveDirection)
		{
			move = newMoveDirection;
		} 

		public void LookInput(Vector2 newLookDirection)
		{
			look = (CursorState.LookEnabled && cursorInputForLook) ? newLookDirection : Vector2.zero;
		}

		public void JumpInput(bool newJumpState)
		{
			jump = newJumpState;
		}

		public void SprintInput(bool newSprintState)
		{
			sprint = newSprintState;
		}
		
		private void OnDisable()
		{
			look = Vector2.zero;
		}

		private void OnApplicationFocus(bool hasFocus)
		{
			CursorState.NotifyApplicationFocus(hasFocus, this);
		}

		private void SetCursorState(bool newState)
		{
			CursorState.SetLocked(newState, this);
		}
	}
	
}