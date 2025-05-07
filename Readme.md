
# 🔁 Share Record On Create Plugin Documentation

This Dynamics 365 Dataverse plugin dynamically shares or revokes access to CRM records based on user geography (such as "pincode" or "region") and organizational role-segment mappings.

---

## ✅ Features

- Grants access to CRM records based on:
  - User-geography assignments (e.g., mapped pincodes/regions)
  - Segment and Role hierarchy (e.g., Field Rep, Manager, Admin)
- Revokes access when geography assignments change
- Assigns default owner if no change flag is set
- Grants extended access to the record creator and the plugin trigger user
- Supports multiple lines of business/geography logic (e.g., region, plant, depot)
- Uses secure impersonation for privileged operations
- Maintains record-level permission integrity automatically

---

## 📈 Business Logic Flow

### 1. **Initialization**
- Loads execution context, tracing, and CRM services
- Retrieves optional impersonation context via environment variable
- Confirms plugin depth to avoid self-recursion

### 2. **Entity Detection**
- Identifies linked entity from input images (e.g., application, deal, request)
- Captures segment ID and reference to the main entity

### 3. **Change Detection**
- If the record's geography (pincode or region) has changed:
  - Revoke access from previously mapped users
  - Trigger downstream permission cleanup for related hierarchies

### 4. **Access Assignment**
- Based on current pincode or region:
  - Retrieve eligible users via custom geography mapping
  - Grant record access to these users
  - Optionally assign a default owner based on role priority

### 5. **Owner Assignment**
- When ownership has not been manually changed:
  - Select first available user from priority role list
  - Update record ownership and flag as changed
  - Grant elevated access to original creator and triggering user

---

## 🔒 Security Requirements

- Plugin must have privileges: Read, Write, Append, Share, Assign
- Should be registered on Create/Update of main transactional entities
- Requires input images (`PreImage`, `PostImage`) to detect geography updates

---

## 🧠 Technical Design Notes

- Uses Dataverse SDK services:
  - Access control (`GrantAccessRequest`, `RevokeAccessRequest`)
  - User mappings retrieved through FetchXML or QueryExpression
- Applies geography mappings and hierarchy definitions:
  - Custom geography map for roles (e.g., city/plant/depot-based logic)
  - Supports multiple segment-role mapping policies

---

## 📁 Suggested Registration Settings

| Plugin Step    | Message | Entity Scope   | Images        | Fields Monitored |
|----------------|---------|----------------|---------------|------------------|
| Create         | Create  | Application    | PostImage     | geography fields |
| Update         | Update  | Application    | Pre+PostImage | geography fields |
| Create         | Create  | Deal           | PostImage     | geography fields |
| Update         | Update  | Opportunity    | Pre+PostImage | geography fields |

---

## 🧪 Example Scenario

If a record's assigned "pincode" changes:

1. Plugin removes access for previously mapped users.
2. Pulls all users newly mapped to the updated location.
3. Grants them access to the record.
4. If no owner was set manually, assigns owner based on role priority.

---


