// Subagent: policy-guardrails — monthly RG budget with 50/80/100% alerts
targetScope = 'resourceGroup'

@description('Monthly budget cap in USD')
@minValue(50)
param monthlyAmountUsd int = 500

@description('Distribution list for budget alerts')
param contactEmails array

@description('Budget start date in YYYY-MM-01 format')
param startDate string = utcNow('yyyy-MM-01')

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: 'squad-rg-monthly'
  properties: {
    category: 'Cost'
    amount: monthlyAmountUsd
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: startDate
    }
    notifications: {
      Actual_50: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 50
        thresholdType: 'Actual'
        contactEmails: contactEmails
      }
      Actual_80: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        thresholdType: 'Actual'
        contactEmails: contactEmails
      }
      Forecasted_100: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: contactEmails
      }
    }
  }
}
