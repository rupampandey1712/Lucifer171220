// Azure Front Door Premium + WAF: one global entry point for the SPA (/*) and the API (/api/*, /hubs/*).
param name string
param tags object
param apiHost string
param webHost string

resource profile 'Microsoft.Cdn/profiles@2024-02-01' = {
  name: 'afd-${name}'
  location: 'global'
  tags: tags
  sku: { name: 'Premium_AzureFrontDoor' }
}

resource endpoint 'Microsoft.Cdn/profiles/afdEndpoints@2024-02-01' = {
  parent: profile
  name: 'ep-${name}'
  location: 'global'
  properties: { enabledState: 'Enabled' }
}

resource waf 'Microsoft.Network/FrontDoorWebApplicationFirewallPolicies@2024-02-01' = {
  name: 'waf${replace(name, '-', '')}'
  location: 'global'
  sku: { name: 'Premium_AzureFrontDoor' }
  properties: {
    policySettings: { enabledState: 'Enabled', mode: 'Prevention' }
    managedRules: { managedRuleSets: [
      { ruleSetType: 'Microsoft_DefaultRuleSet', ruleSetVersion: '2.1', ruleSetAction: 'Block' }
      { ruleSetType: 'Microsoft_BotManagerRuleSet', ruleSetVersion: '1.0' }
    ] }
  }
}

resource security 'Microsoft.Cdn/profiles/securityPolicies@2024-02-01' = {
  parent: profile
  name: 'waf'
  properties: {
    parameters: {
      type: 'WebApplicationFirewall'
      wafPolicy: { id: waf.id }
      associations: [{ domains: [{ id: endpoint.id }], patternsToMatch: ['/*'] }]
    }
  }
}

resource webGroup 'Microsoft.Cdn/profiles/originGroups@2024-02-01' = {
  parent: profile
  name: 'web'
  properties: { loadBalancingSettings: { sampleSize: 4, successfulSamplesRequired: 3 }, healthProbeSettings: { probePath: '/', probeProtocol: 'Https', probeRequestType: 'HEAD', probeIntervalInSeconds: 120 } }
}

resource apiGroup 'Microsoft.Cdn/profiles/originGroups@2024-02-01' = {
  parent: profile
  name: 'api'
  properties: { loadBalancingSettings: { sampleSize: 4, successfulSamplesRequired: 3 }, healthProbeSettings: { probePath: '/health/live', probeProtocol: 'Https', probeRequestType: 'GET', probeIntervalInSeconds: 60 } }
}

resource webOrigin 'Microsoft.Cdn/profiles/originGroups/origins@2024-02-01' = {
  parent: webGroup
  name: 'swa'
  properties: { hostName: webHost, originHostHeader: webHost, httpsPort: 443, priority: 1, weight: 1000 }
}

resource apiOrigin 'Microsoft.Cdn/profiles/originGroups/origins@2024-02-01' = {
  parent: apiGroup
  name: 'containerapp'
  properties: { hostName: apiHost, originHostHeader: apiHost, httpsPort: 443, priority: 1, weight: 1000 }
}

resource apiRoute 'Microsoft.Cdn/profiles/afdEndpoints/routes@2024-02-01' = {
  parent: endpoint
  name: 'api'
  dependsOn: [apiOrigin]
  properties: { originGroup: { id: apiGroup.id }, patternsToMatch: ['/api/*', '/hubs/*', '/health/*'], forwardingProtocol: 'HttpsOnly', httpsRedirect: 'Enabled', supportedProtocols: ['Https', 'Http'] }
}

resource webRoute 'Microsoft.Cdn/profiles/afdEndpoints/routes@2024-02-01' = {
  parent: endpoint
  name: 'web'
  dependsOn: [webOrigin, apiRoute]
  properties: {
    originGroup: { id: webGroup.id }
    patternsToMatch: ['/*']
    forwardingProtocol: 'HttpsOnly'
    httpsRedirect: 'Enabled'
    supportedProtocols: ['Https', 'Http']
    cacheConfiguration: { queryStringCachingBehavior: 'IgnoreQueryString', compressionSettings: { isCompressionEnabled: true, contentTypesToCompress: ['text/html', 'application/javascript', 'text/css', 'image/svg+xml'] } }
  }
}

output endpoint string = 'https://${endpoint.properties.hostName}'
