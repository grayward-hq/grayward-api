using Domain.Entities;
using Domain.Enums;

namespace Application.Interfaces;

public interface IPlanCatalog 
{ 
    Plan Get(PlanCode code); 
}