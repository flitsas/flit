import type {
  ConformationRuleItem,
  FieldType,
  FormFieldInput,
  FormFieldItem,
  ProcedureSection,
  ProcedureSectionInput,
  ProcedureStep,
  ProcedureStepInput,
} from './types/procedure-parametrization';

export function mapStepsFromApi(dtos: ProcedureStep[]): ProcedureStep[] {
  return dtos.map((s) => ({
    id: s.id,
    code: s.code,
    title: s.title,
    sortOrder: s.sortOrder,
    isActive: s.isActive ?? true,
    sections: (s.sections ?? []).map((sec) => ({
      id: sec.id,
      code: sec.code,
      title: sec.title,
      sortOrder: sec.sortOrder,
      layout: sec.layout ?? 'single',
      formFields: (sec.formFields ?? []).map((f) => ({
        id: f.id,
        fieldKey: f.fieldKey,
        label: f.label,
        fieldType: f.fieldType as FieldType,
        isRequired: f.isRequired,
        sortOrder: f.sortOrder,
        isLocked: f.isLocked,
        lockReason: f.lockReason,
        consultationTemplateId: f.consultationTemplateId,
        options: f.options,
        validationSchema: f.validationSchema,
      })),
    })),
  }));
}

function toFormFieldInput(field: FormFieldItem, index: number): FormFieldInput {
  const options =
    field.options == null
      ? null
      : typeof field.options === 'string'
        ? field.options
        : JSON.stringify(field.options);
  const validationSchema =
    field.validationSchema == null
      ? null
      : typeof field.validationSchema === 'string'
        ? field.validationSchema
        : JSON.stringify(field.validationSchema);

  return {
    fieldKey: field.fieldKey,
    label: field.label,
    fieldType: field.fieldType,
    isRequired: field.isRequired,
    sortOrder: field.sortOrder ?? index,
    options,
    validationSchema,
    defaultValue: null,
  };
}

function defaultSectionsForStep(
  stepIndex: number,
  vehicleActive: boolean,
): ProcedureSection[] {
  if (stepIndex === 0 && vehicleActive) {
    return [
      {
        code: 'vehicle_data',
        title: 'Vehículo',
        sortOrder: 1,
        layout: 'single',
        formFields: [],
      },
    ];
  }
  return [
    {
      code: `section_${stepIndex + 1}`,
      title: 'Datos generales',
      sortOrder: 1,
      layout: 'single',
      formFields: [],
    },
  ];
}

export function buildStepInputsForSave(
  steps: ProcedureStep[],
  conformationRules: ConformationRuleItem[],
): ProcedureStepInput[] {
  const vehicleActive = conformationRules.some(
    (r) => r.procedureEntityCode === 'VEHICLE' && r.isActive,
  );

  return steps.map((step, index) => {
    const sections =
      step.sections.length > 0
        ? step.sections
        : defaultSectionsForStep(index, vehicleActive);

    return {
      code: step.code || `step_${index + 1}`,
      title: step.title,
      sortOrder: step.sortOrder,
      isActive: step.isActive ?? true,
      sections: sections.map((sec, secIndex) => toSectionInput(sec, secIndex)),
    };
  });
}

function toSectionInput(section: ProcedureSection, index: number): ProcedureSectionInput {
  return {
    code: section.code,
    title: section.title,
    sortOrder: section.sortOrder ?? index + 1,
    layout: section.layout ?? 'single',
    formFields: (section.formFields ?? []).map(toFormFieldInput),
  };
}

export function findVehicleSection(steps: ProcedureStep[]): ProcedureSection | undefined {
  for (const step of steps) {
    const vehicleSection = step.sections.find((s) => s.code === 'vehicle_data');
    if (vehicleSection?.id) return vehicleSection;
    const firstWithId = step.sections.find((s) => s.id);
    if (firstWithId) return firstWithId;
  }
  return undefined;
}

export function hasLockedPlateOrVin(steps: ProcedureStep[]): boolean {
  return steps.some((step) =>
    step.sections.some((sec) =>
      sec.formFields.some((f) => f.fieldKey === 'plate_or_vin' && f.isLocked),
    ),
  );
}
