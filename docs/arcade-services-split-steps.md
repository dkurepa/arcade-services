This document will attempt to describe the steps we'll need to follow during the charter alignment epic:
- First of all, we will need to create a new entry in Service Tree. Service tree is the herat of organizing services within Microsoft. After it is created, a new, corresponding service will be created on S360 too. All s360 items for the Service Tree entry will be displayed here. The service is configured on service tree itself: things like which repo is owned by the service, if it's "production or not" and other things. List of information we need to create a service:
    - Organization name: Service Group or Team Group <span style="color:red">do we want to create a new one? An organization needs Admins and Business owners</span>
    - Service type: https://eng.ms/docs/cloud-ai-platform/azure-edge-platform-aep/aep-engineering-systems/engineering-intelligence-standards/service-tree/service-trees-team-doc/stbasics/type-svcs <span style="color:red">based on the information on the url, I think our service type is Online Azure </span>
    - Specify the clouds in which our services will be deployed <span style="color:red">this should be public, I think</span>
    - Name: a full, meaningful name without any acronyms
    - Short Name: an abbreviation or acronym representing our service name
    - Description: a meaningful description from which a naive user should be able to understand what the service does
    - Admins <span style="color:red">Tomas for sure, maybe someone else from the team, Helix has Chris. Ilya, Mat Gal, Stu and Jon, for example</span>
    - PM Owner <span style="color:red">Ilya has this role on the Helix service</span>
    - Dev Owners <span style="color:red">the team?</span>
    - Feature Team Alias: Distribution list alias that is best used by customers to contact the team <span style="color:red">tokaprep (Tomas Kapin Direct Reports)? it's helixadmins in he Helix service</span>
    - Built By Microsoft: is our service developed or owned by Microsoft <span style="color:red">yes</span>
    - External Facing: Indicate if your service has external customer impact <span style="color:red">Yes, I think</span>
    - Tags: Optional, a few keywords to help our service be more discoverable when searching
    - Metadata:
        - BCDR Champ: A representative from our team who will serve as the primary contact for Business Continuity/Disaster Recovery (BCDR) and will be responsible for entering data in BCDR Manager (aka.ms/bcdr). Only one contact is permitted. <span style="color:red">chrisboh in Helix, so tkapin for us?</span>
        - Compliance Contact: Alias for our service's Compliance Contact. Only one alias is permitted. <span style="color:red">chrisboh in Helix, so tkapin for us?</span>
        - Privacy Champ: Alias for our Privacy Champ. Only one alias is permitted. <span style="color:red">chrisboh in Helix, so tkapin for us?</span>
        - Have you registered all GDPR data hosted outside of Geneva: https://microsoft.sharepoint.com/teams/Azure_Compliance/GDPR/GDPR%20Wiki/Register%20Non-Geneva%20Assets.aspx <span style="color:red">set to Not Applicable in the Helix subscription, I guess it should remain the same as we will be using the same data</span>
        - Will an outage of your service adversely impact any other product or service with whom you have any SLAs <span style="color:red">yes, it's Maestro</span>

    This was the full list of data needed to **create** the service. However there's a lot of other information configured on the Helix repo that doesn't appear to be required. We should talk to Ilya about it, as he seems to a lot of knowledge in this area.
- After the Service Tree service has been created, we should add the following metadata (this list is not extensive, just think I knew of):
    - Repositories owned by the Service (Azdo Mirrors)
    - Pipelines owned by the Service
    - Service Endpoints
    - Azure subscriptions owned by the service (we will have to create this first)
- Do we need to create components for our Service Tree Subscription? If in the future we create more services (besides Maestro), we will need to have them in separate components. It might be a good idea to put Maestro as a Component of the Service