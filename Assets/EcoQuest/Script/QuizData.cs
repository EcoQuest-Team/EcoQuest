using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class QuizQuestion
{
    [TextArea(2, 4)]
    public string questionText;

    [Tooltip("List of answer choices (3 or 4 choices)")]
    public string[] options;

    [Tooltip("0-based index of the correct answer in the original options array")]
    public int correctOptionIndex;

    [TextArea(2, 3)]
    [Tooltip("One-line explanation displayed during post-quiz feedback")]
    public string explanation;

    [Tooltip("Optional illustration displayed inside the left wooden cutout frame")]
    public Sprite questionImage;
}

[CreateAssetMenu(fileName = "QuizData", menuName = "EcoQuest/Quiz Data", order = 1)]
public class QuizData : ScriptableObject
{
    [Header("Quiz Questions")]
    public List<QuizQuestion> questions = new List<QuizQuestion>();
}
