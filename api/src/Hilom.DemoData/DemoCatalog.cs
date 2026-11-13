namespace Hilom.DemoData;

/// <summary>Plain words the generators draw from. Every person, address and number made from them is fictional.</summary>
internal static class DemoCatalog
{
    public static readonly string[] FemaleNames =
    [
        "Maria", "Ana", "Liza", "Carla", "Jasmine", "Teresita", "Lourdes", "Rosario", "Angelica", "Patricia", "Cristina", "Bernadette",
        "Josefina", "Mariel", "Corazon", "Divina", "Elena", "Fe", "Gloria", "Imelda", "Joy", "Katrina", "Luz", "Marites", "Nenita",
        "Olivia", "Pilar", "Queenie", "Rowena", "Sheila", "Trisha", "Vilma", "Yolanda", "Zenaida", "Beatriz", "Charlene", "Dolores",
    ];

    public static readonly string[] MaleNames =
    [
        "Juan", "Jose", "Antonio", "Ramon", "Carlo", "Miguel", "Rafael", "Eduardo", "Fernando", "Gabriel", "Henry", "Ismael", "Jun",
        "Kevin", "Leonardo", "Manuel", "Noel", "Oscar", "Pedro", "Rodrigo", "Santiago", "Tomas", "Victor", "Wilfredo", "Arnel",
        "Benjamin", "Cesar", "Danilo", "Emilio", "Felipe", "Gerardo", "Hector", "Ignacio", "Jerome", "Lorenzo", "Mario",
    ];

    public static readonly string[] Surnames =
    [
        "Santos", "Reyes", "Cruz", "Bautista", "Ocampo", "Garcia", "Mendoza", "Torres", "Villanueva", "Dizon", "Navarro", "Aquino",
        "Castillo", "Domingo", "Espiritu", "Flores", "Gonzales", "Hernandez", "Ignacio", "Jimenez", "Lacson", "Manalo", "Natividad",
        "Pascual", "Quizon", "Ramos", "Salazar", "Tolentino", "Urbano", "Valdez", "Yap", "Zamora", "Abad", "Bernardo", "Concepcion",
        "De Guzman", "Estrada", "Fernandez", "Gutierrez", "Lim", "Macaraeg", "Padilla", "Rivera", "Sison", "Tan", "Velasco", "Alcantara",
    ];

    public static readonly string[] Streets =
    [
        "Sampaguita", "Narra", "Acacia", "Molave", "Ilang-Ilang", "Mabini", "Rizal", "Bonifacio", "Luna", "Aguinaldo", "Del Pilar", "Kalayaan",
    ];

    public static readonly string[] Barangays =
    [
        "Maligaya", "Bagong Pag-asa", "San Isidro", "Santa Cruz", "Poblacion", "Malinis", "Masagana", "Tahimik", "Bagumbayan", "Masikap",
    ];

    public static readonly string[] Religions = ["Roman Catholic", "Roman Catholic", "Roman Catholic", "Iglesia ni Cristo", "Islam", "Born Again Christian", "Aglipayan"];

    public static readonly string[] Departments =
    [
        "Internal Medicine", "Surgery", "Pediatrics", "Obstetrics and Gynecology", "Cardiology", "Intensive Care", "Nursing Administration",
        "Human Resources", "Admitting and Records",
    ];

    public static readonly string[] Positions =
    [
        "Consultant Physician", "Resident Physician", "Staff Nurse", "Head Nurse", "Chief Nurse", "HR Officer", "Front Desk Clerk", "Hospital Administrator",
    ];

    public static readonly string[] Specializations =
    [
        "Internal Medicine", "Cardiology", "Pediatrics", "General Surgery", "Obstetrics and Gynecology", "Family Medicine", "Neurology", "Orthopedics",
    ];

    /// <summary>Specialization for each physician number, and the department they sit in.</summary>
    public static readonly (string Specialization, string Department)[] PhysicianPlacement =
    [
        ("Internal Medicine", "Internal Medicine"), ("Cardiology", "Cardiology"), ("Pediatrics", "Pediatrics"), ("General Surgery", "Surgery"),
        ("Obstetrics and Gynecology", "Obstetrics and Gynecology"), ("Family Medicine", "Internal Medicine"), ("Internal Medicine", "Internal Medicine"),
        ("Neurology", "Internal Medicine"), ("Orthopedics", "Surgery"), ("Cardiology", "Cardiology"), ("Pediatrics", "Pediatrics"),
        ("Internal Medicine", "Internal Medicine"), ("Family Medicine", "Internal Medicine"), ("General Surgery", "Surgery"),
    ];

    /// <summary>Wards in order. "Ward 3" is the medical ward the demo nurses work on, as in the main flow of docs/ARCHITECTURE.md.</summary>
    public static readonly (string Code, string Name, Hilom.Facility.Domain.WardType Type, string Department)[] Wards =
    [
        ("W1", "Ward 1 Surgical", Hilom.Facility.Domain.WardType.Surgical, "Surgery"),
        ("W2", "Ward 2 Pediatrics", Hilom.Facility.Domain.WardType.Pediatric, "Pediatrics"),
        ("W3", "Ward 3 Medical", Hilom.Facility.Domain.WardType.General, "Internal Medicine"),
        ("ICU", "Intensive Care Unit", Hilom.Facility.Domain.WardType.Icu, "Intensive Care"),
        ("W5", "Ward 5 Medical", Hilom.Facility.Domain.WardType.General, "Internal Medicine"),
        ("MAT", "Maternity Ward", Hilom.Facility.Domain.WardType.Maternity, "Obstetrics and Gynecology"),
        ("W7", "Ward 7 Cardiac", Hilom.Facility.Domain.WardType.General, "Cardiology"),
        ("ISO", "Isolation Ward", Hilom.Facility.Domain.WardType.Isolation, "Internal Medicine"),
        ("W9", "Ward 9 Surgical", Hilom.Facility.Domain.WardType.Surgical, "Surgery"),
    ];

    public static readonly string[] ClinicReasons =
    [
        "Follow-up for high blood pressure", "Persistent cough for two weeks", "Fever and body aches", "Annual check-up", "Chest tightness on exertion",
        "Review of laboratory results", "Lower back pain", "Dizziness when standing", "Skin rash on the arms", "Diabetes follow-up and medication review",
        "Abdominal pain after meals", "Headaches for several days", "Swelling in both ankles", "Difficulty sleeping", "Sore throat and difficulty swallowing",
        "Wound check after minor procedure", "Child with fever and poor appetite", "Prenatal check-up", "Knee pain when climbing stairs", "Medication refill",
    ];

    public static readonly string[] AdmissionDiagnoses =
    [
        "Community-acquired pneumonia", "Acute decompensated heart failure", "Uncontrolled type 2 diabetes with ketosis", "Acute gastroenteritis with dehydration",
        "Urinary tract infection with sepsis", "Hypertensive urgency", "Dengue fever with warning signs", "Post-operative care after appendectomy",
        "Chronic kidney disease, fluid overload", "Acute exacerbation of asthma", "Cellulitis of the lower leg", "Unstable angina",
        "Ischemic stroke, mild", "Fractured femur, awaiting fixation", "Pre-eclampsia", "Bronchiolitis",
    ];

    public static readonly string[] AssessmentNotes =
    [
        "Patient is alert and comfortable. Vital signs stable. Plan: continue current medications, review labs at the next visit.",
        "Reports improvement since the last visit. No new complaints. Examination unremarkable. Advised on diet and follow-up in four weeks.",
        "Symptoms consistent with a viral upper respiratory infection. Advised fluids, rest and paracetamol as needed. Return if fever persists beyond three days.",
        "Blood pressure above target on two readings. Medication adjusted. Counselled on salt intake. Home blood pressure log requested.",
        "Complains of intermittent pain, worse with movement. No red flags. Started on analgesia and referred for imaging if not better in one week.",
        "Examination notable for mild tenderness. Laboratory tests ordered. Patient understands the plan and agrees to follow up.",
    ];

    public static readonly string[] ProgressNotes =
    [
        "Seen on rounds. Afebrile overnight. Tolerating diet. Continue current treatment, reassess tomorrow.",
        "Improving. Oxygen requirement down. Encourage ambulation. Plan for discharge when stable on room air.",
        "Pain controlled. Wound clean and dry. Vital signs within range. Continue antibiotics, repeat labs in the morning.",
        "Slow progress. Appetite still poor. Dietitian review requested. Family updated at the bedside.",
        "Stable. Renal function improving after fluid adjustment. Continue monitoring intake and output.",
    ];

    public static readonly string[] NursingNotes =
    [
        "Received patient awake, comfortable. Vital signs taken and documented. Medications given as ordered. Call light within reach.",
        "Patient slept well. No complaints of pain. Assisted to the bathroom. Intake and output recorded.",
        "Complained of mild nausea, relieved after repositioning. Physician informed. Continues on monitoring.",
        "Dressing changed, site clean. Patient verbalized understanding of care. Family at the bedside.",
        "Patient ambulated in the corridor with assistance. Tolerated well. Skin intact. Fall precautions maintained.",
        "Restless at the start of the shift, settled after reassurance. Vital signs stable. Endorsed to the next shift.",
    ];

    public static readonly string[] DietNotes =
    [
        "Low-salt, low-fat diet. Tolerating meals, about three quarters eaten.",
        "Diabetic diet. Reviewed carbohydrate portions with the patient and family.",
        "Soft diet, small frequent meals. Fluids encouraged.",
    ];

    public static readonly string[] DischargeSummaries =
    [
        "Admitted with the diagnosis above. Treated and monitored. Improved and stable for discharge. Home medications reviewed, follow-up in the outpatient clinic in one week. Return for worsening symptoms.",
        "Course uneventful. Symptoms resolved. Discharged home with medications and advice on diet and activity. Follow-up with the attending physician in two weeks.",
        "Responded well to treatment. Vital signs normal for 24 hours. Discharged with a written plan and contact numbers.",
    ];

    public static readonly string[] OtherOrders =
    [
        "Complete blood count and basic metabolic panel in the morning.",
        "Chest X-ray, two views, today.",
        "Urinalysis and culture.",
        "ECG now and daily.",
        "Strict intake and output monitoring.",
        "Vital signs every four hours. Notify physician if systolic below 90 or above 180.",
        "Bed rest with bathroom privileges. Fall precautions.",
    ];

    /// <summary>Medication, dose, route, frequency and the hours of the day it's due.</summary>
    public static readonly (string Name, string Dose, string Route, string Frequency, int[] Hours)[] Medications =
    [
        ("Paracetamol", "500 mg", "oral", "every 6 hours as needed", [0, 6, 12, 18]),
        ("Amlodipine", "5 mg", "oral", "once daily", [8]),
        ("Losartan", "50 mg", "oral", "once daily", [8]),
        ("Metformin", "500 mg", "oral", "twice daily with meals", [8, 18]),
        ("Ceftriaxone", "1 g", "intravenous", "every 12 hours", [6, 18]),
        ("Omeprazole", "20 mg", "oral", "once daily before breakfast", [6]),
        ("Furosemide", "40 mg", "intravenous", "once daily", [8]),
        ("Enoxaparin", "40 mg", "subcutaneous", "once daily", [20]),
        ("Salbutamol", "2.5 mg", "inhaled", "every 6 hours", [0, 6, 12, 18]),
        ("Insulin glargine", "10 units", "subcutaneous", "once daily at bedtime", [21]),
        ("Cefuroxime", "500 mg", "oral", "twice daily", [8, 20]),
        ("Ibuprofen", "400 mg", "oral", "every 8 hours with food", [6, 14, 22]),
        ("Simvastatin", "20 mg", "oral", "once daily at bedtime", [21]),
        ("Ondansetron", "4 mg", "intravenous", "every 8 hours as needed", [6, 14, 22]),
        ("Morphine", "2 mg", "intravenous", "every 4 hours as needed", [2, 6, 10, 14, 18, 22]),
    ];

    /// <summary>Allergens as patients' records would name them, with how severe a reaction is typical.</summary>
    public static readonly (string Allergen, Hilom.Patients.Domain.AllergyCategory Category, string Reaction)[] Allergies =
    [
        ("Penicillin", Hilom.Patients.Domain.AllergyCategory.Drug, "Hives and swelling"),
        ("Ibuprofen", Hilom.Patients.Domain.AllergyCategory.Drug, "Wheezing"),
        ("Sulfa drugs", Hilom.Patients.Domain.AllergyCategory.Drug, "Skin rash"),
        ("Shellfish", Hilom.Patients.Domain.AllergyCategory.Food, "Swelling of the lips"),
        ("Peanuts", Hilom.Patients.Domain.AllergyCategory.Food, "Anaphylaxis"),
        ("Dust mites", Hilom.Patients.Domain.AllergyCategory.Environmental, "Sneezing and itchy eyes"),
        ("Latex", Hilom.Patients.Domain.AllergyCategory.Other, "Contact dermatitis"),
        ("Morphine", Hilom.Patients.Domain.AllergyCategory.Drug, "Nausea and itching"),
        ("Egg", Hilom.Patients.Domain.AllergyCategory.Food, "Stomach upset"),
    ];

    public static readonly string[] NotGivenReasons =
    [
        "Patient nil per os for a procedure.", "Patient asleep, rescheduled with physician agreement.", "Blood pressure below the hold parameter.",
        "Patient refused, risks explained.", "Patient vomiting, physician informed.", "Out of stock on the ward, pharmacy called.",
    ];
}
