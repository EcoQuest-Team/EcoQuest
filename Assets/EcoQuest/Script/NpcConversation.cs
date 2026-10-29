using System.Collections;
using UnityEngine;

public class NpcConversation : MonoBehaviour
{
    [SerializeField] private NpcBrain npcA;
    [SerializeField] private NpcBrain npcB;
    [SerializeField] private float minTurnDuration = 4f;
    [SerializeField] private float maxTurnDuration = 9f;
    [SerializeField] private float conversationDistance = 1.35f;

    private Coroutine dialogueCoroutine;
    private bool isActive = false;

    public void SetupPair(NpcBrain a, NpcBrain b, Vector3 centerPos, Vector3 forwardDir)
    {
        npcA = a;
        npcB = b;
        isActive = true;

        Vector3 offset = forwardDir.normalized * (conversationDistance * 0.5f);
        Vector3 posA = centerPos - offset;
        Vector3 posB = centerPos + offset;

        npcA.transform.position = posA;
        npcB.transform.position = posB;

        npcA.transform.rotation = Quaternion.LookRotation(posB - posA);
        npcB.transform.rotation = Quaternion.LookRotation(posA - posB);

        npcA.StartConversation(npcB.transform);
        npcB.StartConversation(npcA.transform);

        dialogueCoroutine = StartCoroutine(ConversationRoutine());
    }

    private IEnumerator ConversationRoutine()
    {
        bool aIsSpeaking = true;

        while (isActive && npcA != null && npcB != null)
        {
            if (aIsSpeaking)
            {
                npcA.SetTalking(true);
                npcB.SetTalking(false);
                if (Random.value < 0.35f) npcB.TriggerIdleGesture(2); // Nod
            }
            else
            {
                npcB.SetTalking(true);
                npcA.SetTalking(false);
                if (Random.value < 0.35f) npcA.TriggerIdleGesture(2); // Nod
            }

            float speakTime = Random.Range(minTurnDuration, maxTurnDuration);
            yield return new WaitForSeconds(speakTime);

            // Natural pause between speaker turns
            npcA.SetTalking(false);
            npcB.SetTalking(false);
            yield return new WaitForSeconds(Random.Range(0.8f, 1.8f));

            aIsSpeaking = !aIsSpeaking;
        }
    }
}
